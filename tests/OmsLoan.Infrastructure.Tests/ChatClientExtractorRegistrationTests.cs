using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// Which providers come up, and what happens to the ones that are not configured.
/// </summary>
/// <remarks>
/// Registration is where "the provider is a configuration value rather than a code path"
/// either holds or quietly stops holding. These construct real clients — they make no calls,
/// so no key needs to be a real one — which means a vendor changing how a client is built
/// fails here rather than on the first notice of the day.
/// </remarks>
public class ChatClientExtractorRegistrationTests
{
    private static ExtractionOptions Configured(params string[] names)
    {
        var options = new ExtractionOptions { DefaultProvider = "Claude" };

        foreach (var name in names)
        {
            options.Providers[name] = new ProviderOptions
            {
                ApiKey = "test-key-not-used",
                ModelId = name.ToLowerInvariant() + "-model",
                SendsPdfNatively = name != ChatClientExtractorRegistration.Groq,
            };
        }

        return options;
    }

    private static INoticeExtractorSelector Build(ExtractionOptions options)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(options));
        services.AddChatClientExtractors(options);

        return services.BuildServiceProvider().GetRequiredService<INoticeExtractorSelector>();
    }

    [Fact]
    public void AllThreeProvidersRegisterWhenConfigured()
    {
        var selector = Build(Configured("Claude", "OpenAi", "Groq"));

        Assert.Equal(["Claude", "OpenAi", "Groq"], selector.Available);
    }

    /// <summary>
    /// #10's third criterion, and the reason it is written down: Groq is the optional one, and
    /// a system that refused to start without it would make the cheap experimental provider a
    /// hard dependency of the daily pipeline.
    /// </summary>
    [Fact]
    public void AnUnconfiguredGroqDisablesItselfAndLeavesTheOthersWorking()
    {
        var selector = Build(Configured("Claude", "OpenAi"));

        Assert.Null(selector.TryGet(ChatClientExtractorRegistration.Groq));
        Assert.Equal(["Claude", "OpenAi"], selector.Available);
        Assert.NotNull(selector.Get());
    }

    /// <summary>
    /// A key without a model id, or the reverse, is a half-finished deployment rather than a
    /// provider. Registering it would let a caller resolve something certain to fail, and the
    /// failure would read as a vendor outage.
    /// </summary>
    [Fact]
    public void AHalfConfiguredProviderIsNotRegistered()
    {
        var options = Configured("Claude");
        options.Providers["Groq"] = new ProviderOptions { ApiKey = "k", ModelId = string.Empty };
        options.Providers["OpenAi"] = new ProviderOptions { ApiKey = string.Empty, ModelId = "gpt-5-mini" };

        var selector = Build(options);

        Assert.Equal(["Claude"], selector.Available);
    }

    /// <summary>
    /// Every registration is wrapped, so the one-attempt rule and the deadline apply to all
    /// three without any of them having to remember.
    /// </summary>
    [Fact]
    public void EveryProviderIsWrappedInTheGuard()
    {
        var selector = Build(Configured("Claude", "OpenAi", "Groq"));

        foreach (var name in selector.Available)
        {
            Assert.IsType<GuardedNoticeExtractor>(selector.Get(name));
        }
    }

    [Fact]
    public void TheModelIdReachesTheExtractor()
    {
        var selector = Build(Configured("Claude", "Groq"));

        Assert.Equal("claude-model", selector.Get("Claude").ModelName);
        Assert.Equal("groq-model", selector.Get("Groq").ModelName);
    }

    /// <summary>
    /// The text extractor is registered once and shared. Groq needs it; the others resolve it
    /// and never call it.
    /// </summary>
    [Fact]
    public void APdfTextExtractorIsAvailableForTheProvidersThatNeedOne()
    {
        var services = new ServiceCollection();
        var options = Configured("Groq");

        services.AddSingleton(Options.Create(options));
        services.AddChatClientExtractors(options);

        var provider = services.BuildServiceProvider();

        Assert.IsType<PdfPigTextExtractor>(provider.GetRequiredService<IPdfTextExtractor>());
    }

    // An "unexpected failure becomes a row" test belongs here in principle, and there is
    // deliberately not one, because the only way to reach the guard through a real
    // registration is to let a real client actually try — which means an outbound call to a
    // vendor with a bogus key on every test run. That is a slow, flaky, network-dependent
    // unit test that spends somebody's rate limit to prove something already proved twice:
    // EveryProviderIsWrappedInTheGuard above asserts every registration is wrapped, and
    // GuardedNoticeExtractorTests in Domain asserts the wrapper converts everything —
    // including NullReferenceException — into a recorded outcome. The two together are the
    // guarantee; a third test that dials out adds cost and no coverage.

    [Fact]
    public void NothingIsRegisteredWhenNothingIsConfigured()
    {
        var selector = Build(new ExtractionOptions());

        Assert.Empty(selector.Available);
        Assert.Throws<InvalidOperationException>(() => selector.Get());
    }
}
