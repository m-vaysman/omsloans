using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OmsLoan.Domain.Extractors;
using OpenAI;

namespace OmsLoan.Infrastructure.Extraction;

/// <summary>
/// Registers the providers: three clients, one extractor.
/// </summary>
/// <remarks>
/// <para>
/// Per #68. Each provider contributes an <see cref="IChatClient"/> and they all share
/// <see cref="ChatClientNoticeExtractor"/>, so adding a fourth vendor that speaks either API
/// is a configuration entry and a line here rather than a new implementation.
/// </para>
/// <para>
/// Registration is conditional throughout. <c>AddNoticeExtractor</c> refuses a provider with
/// no key or no model id, so an unconfigured Groq disables itself and leaves the others
/// working — which is what #10 asks for, and matters because Groq is the optional one.
/// </para>
/// </remarks>
public static class ChatClientExtractorRegistration
{
    /// <summary>The names the configuration section uses.</summary>
    public const string Claude = "Claude";
    public const string OpenAi = "OpenAi";
    public const string Groq = "Groq";

    /// <summary>Groq's OpenAI-compatible endpoint, used when configuration does not give one.</summary>
    private const string GroqDefaultEndpoint = "https://api.groq.com/openai/v1";

    /// <summary>
    /// Registers every configured provider and returns the names that took.
    /// </summary>
    public static IReadOnlyList<string> AddChatClientExtractors(
        this IServiceCollection services,
        ExtractionOptions extraction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extraction);

        services.AddNoticeExtraction();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

        var registered = new List<string>();

        if (services.AddNoticeExtractor(extraction, Claude, CreateClaude)) registered.Add(Claude);
        if (services.AddNoticeExtractor(extraction, OpenAi, CreateOpenAi)) registered.Add(OpenAi);
        if (services.AddNoticeExtractor(extraction, Groq, CreateGroq)) registered.Add(Groq);

        return registered;
    }

    /// <summary>
    /// Claude, through Anthropic's own client.
    /// </summary>
    /// <remarks>
    /// Not through the OpenAI adapter. Anthropic publishes an OpenAI-compatible endpoint and
    /// pointing this at it would make all three providers look identical in code — at the cost
    /// of native PDF input, prompt caching and citations, which is the whole reason ADR 0001
    /// picked Claude. The uniformity would be real and the capability loss would be silent.
    /// </remarks>
    private static INoticeExtractor CreateClaude(IServiceProvider services, ProviderOptions provider)
    {
        var client = new AnthropicClient
        {
            ApiKey = provider.ApiKey,

            // Zero, deliberately. The SDK retries by default, and #7 settled that there is
            // exactly one attempt per extraction: a failure is recorded and a reviewer decides
            // what to do. A retry policy inside the client would be invisible from outside,
            // would multiply the cost of an outage, and would make the recorded latency the
            // sum of attempts nobody knows happened.
            MaxRetries = 0,
        };

        return new ChatClientNoticeExtractor(
            client.AsIChatClient(provider.ModelId, provider.MaxTokens),
            provider);
    }

    private static INoticeExtractor CreateOpenAi(IServiceProvider services, ProviderOptions provider) =>
        FromOpenAiApi(services, provider, defaultEndpoint: null);

    /// <summary>
    /// Groq, which is the OpenAI client pointed somewhere else.
    /// </summary>
    /// <remarks>
    /// No package of its own. <c>GroqSharp</c> exists and has been stale since 2024; adding a
    /// dependency to change a base address would be the expensive way to spell one string.
    /// </remarks>
    private static INoticeExtractor CreateGroq(IServiceProvider services, ProviderOptions provider) =>
        FromOpenAiApi(services, provider, GroqDefaultEndpoint);

    private static INoticeExtractor FromOpenAiApi(
        IServiceProvider services,
        ProviderOptions provider,
        string? defaultEndpoint)
    {
        var endpoint = string.IsNullOrWhiteSpace(provider.BaseUrl) ? defaultEndpoint : provider.BaseUrl;

        var options = new OpenAIClientOptions
        {
            // Same rule as Claude, different library spelling it. System.ClientModel installs
            // a retrying pipeline unless told otherwise, so leaving this alone would have put
            // retries back under a system whose whole design says there are none.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        if (endpoint is not null)
        {
            options.Endpoint = new Uri(endpoint);
        }

        var client = new OpenAIClient(new ApiKeyCredential(provider.ApiKey), options);

        return new ChatClientNoticeExtractor(
            client.GetChatClient(provider.ModelId).AsIChatClient(),
            provider,
            // Resolved rather than required: a provider configured as taking PDFs natively
            // never asks for one, and one that cannot will fail saying so.
            services.GetService<IPdfTextExtractor>());
    }
}
