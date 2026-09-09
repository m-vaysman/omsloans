using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// Resolving a provider by name — the thing that makes provider choice a configuration value
/// rather than a code path, and therefore what reprocessing and the accuracy report are built
/// on.
/// </summary>
public class NoticeExtractorSelectorTests
{
    private static ExtractionOptions Options(string defaultProvider = "Claude", params string[] configured)
    {
        var options = new ExtractionOptions { DefaultProvider = defaultProvider };

        foreach (var name in configured.Length > 0 ? configured : ["Claude", "Groq"])
        {
            options.Providers[name] = new ProviderOptions { ApiKey = "k", ModelId = $"{name.ToLowerInvariant()}-1" };
        }

        return options;
    }

    private static INoticeExtractorSelector Build(ExtractionOptions options)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddNoticeExtraction();

        foreach (var name in options.Providers.Keys)
        {
            services.AddNoticeExtractor(
                options, name, (_, provider) => new FakeNoticeExtractor(provider.ModelId));
        }

        return services.BuildServiceProvider().GetRequiredService<INoticeExtractorSelector>();
    }

    [Fact]
    public void NoNameGivenResolvesTheConfiguredDefault()
    {
        var selector = Build(Options(defaultProvider: "Groq"));

        Assert.Equal("groq-1", selector.Get().ModelName);
    }

    [Fact]
    public void AProviderCanBeResolvedByName()
    {
        var selector = Build(Options());

        Assert.Equal("claude-1", selector.Get("Claude").ModelName);
        Assert.Equal("groq-1", selector.Get("Groq").ModelName);
    }

    /// <summary>
    /// Throws rather than falling back. A reprocess run that asked for one provider and was
    /// silently given another produces rows labelled with a model that never saw the notice —
    /// which is worse than not running, because the accuracy report then compares two things
    /// that are the same thing.
    /// </summary>
    [Fact]
    public void AnUnknownProviderThrowsRatherThanFallingBackToTheDefault()
    {
        var selector = Build(Options());

        var ex = Assert.Throws<InvalidOperationException>(() => selector.Get("Gemini"));

        Assert.Contains("Gemini", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Claude", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetReturnsNullForAnUnknownProvider()
    {
        var selector = Build(Options());

        Assert.Null(selector.TryGet("Gemini"));
        Assert.NotNull(selector.TryGet("Claude"));
    }

    /// <summary>
    /// An unconfigured provider is not registered at all. Registering it would let a caller
    /// resolve something certain to fail on its first call, and the failure would read as a
    /// provider outage rather than a deployment nobody finished.
    /// </summary>
    [Fact]
    public void AProviderWithoutAKeyIsNotRegistered()
    {
        var options = Options();
        options.Providers["OpenAi"] = new ProviderOptions { ModelId = "gpt-x", ApiKey = string.Empty };

        var selector = Build(options);

        Assert.Null(selector.TryGet("OpenAi"));
        Assert.DoesNotContain("OpenAi", selector.Available);
    }

    [Fact]
    public void AProviderWithoutAModelIdIsNotRegistered()
    {
        var options = Options();
        options.Providers["OpenAi"] = new ProviderOptions { ApiKey = "k", ModelId = string.Empty };

        var selector = Build(options);

        Assert.Null(selector.TryGet("OpenAi"));
    }

    [Fact]
    public void AvailableListsOnlyProvidersThatAreBothConfiguredAndRegistered()
    {
        var options = Options();
        options.Providers["OpenAi"] = new ProviderOptions { ApiKey = string.Empty, ModelId = string.Empty };

        Assert.Equal(["Claude", "Groq"], Build(options).Available);
    }

    /// <summary>
    /// Every registration is wrapped, so no provider has to remember to bound its own call or
    /// to turn its own exceptions into a recorded failure.
    /// </summary>
    [Fact]
    public async Task EveryRegisteredProviderIsWrappedInTheGuard()
    {
        var options = new ExtractionOptions { DefaultProvider = "Claude" };
        options.Providers["Claude"] = new ProviderOptions { ApiKey = "k", ModelId = "claude-1" };

        var inner = new FakeNoticeExtractor("claude-1").Throws(new InvalidOperationException("boom"));

        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddNoticeExtraction();
        services.AddNoticeExtractor(options, "Claude", (_, _) => inner);

        var selector = services.BuildServiceProvider().GetRequiredService<INoticeExtractorSelector>();

        // The bare fake would have thrown straight out; the guard turns it into a row.
        var result = await selector.Get().ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Available is asked of the container, not of configuration. A provider can be configured
    /// and never registered — its implementation is not written yet — and reporting it as
    /// available would make a reprocess loop skip it silently.
    /// </summary>
    [Fact]
    public void AConfiguredProviderWithNoImplementationIsNotReportedAsAvailable()
    {
        var options = new ExtractionOptions { DefaultProvider = "Claude" };
        options.Providers["Claude"] = new ProviderOptions { ApiKey = "k", ModelId = "claude-1" };
        options.Providers["OpenAi"] = new ProviderOptions { ApiKey = "k", ModelId = "gpt-x" };

        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddNoticeExtraction();

        // Only Claude gets an implementation; OpenAi is configured and unwritten.
        services.AddNoticeExtractor(options, "Claude", (_, _) => new FakeNoticeExtractor("claude-1"));

        var selector = services.BuildServiceProvider().GetRequiredService<INoticeExtractorSelector>();

        Assert.Equal(["Claude"], selector.Available);
        Assert.Contains("OpenAi", options.ConfiguredProviders);
    }

    /// <summary>
    /// The section binds the way the rest of the configuration does, so an operator sets
    /// providers in appsettings and keys in the environment.
    /// </summary>
    [Fact]
    public void TheSectionBindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Extraction:DefaultProvider"] = "Groq",
                ["Extraction:Providers:Groq:ApiKey"] = "gsk-test",
                ["Extraction:Providers:Groq:ModelId"] = "llama-3.3-70b",
                ["Extraction:Providers:Groq:MaxTokens"] = "8192",
                ["Extraction:Providers:Groq:TimeoutSeconds"] = "45",
                ["Extraction:Providers:Claude:ModelId"] = "claude-sonnet-4",
            })
            .Build();

        var options = new ExtractionOptions();
        configuration.GetSection(ExtractionOptions.SectionName).Bind(options);

        Assert.Equal("Groq", options.DefaultProvider);
        Assert.Equal("llama-3.3-70b", options.Providers["Groq"].ModelId);
        Assert.Equal(8192, options.Providers["Groq"].MaxTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Providers["Groq"].Timeout);

        // Claude has a model but no key, so it is present in configuration and not usable.
        Assert.False(options.Providers["Claude"].IsConfigured);
        Assert.Equal(["Groq"], options.ConfiguredProviders);
    }
}
