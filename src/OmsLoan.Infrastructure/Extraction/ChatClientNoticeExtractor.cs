using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Infrastructure.Extraction;

/// <summary>
/// The extractor. One implementation over <see cref="IChatClient"/>, used by every provider.
/// </summary>
/// <remarks>
/// <para>
/// Issues #8, #9 and #10 were written as three implementations — one per vendor — and #68
/// replaced that with this. The reason it works is that the vendors implement the abstraction
/// themselves: Anthropic's official package supplies an <see cref="IChatClient"/>, and
/// <c>Microsoft.Extensions.AI.OpenAI</c> supplies one for OpenAI and for anything else
/// speaking that API. Nothing here is a lowest-common-denominator shim.
/// </para>
/// <para>
/// That distinction is load-bearing rather than tidy. A wrapper that normalised the vendors
/// would have had to normalise document input too, and Claude's native PDF handling is the
/// reason ADR 0001 chose Claude at all — these notices are tabular, and a rate table flattened
/// to text loses which tranche owns which rate. Uniformity bought by giving that up would have
/// cost more than it saved.
/// </para>
/// <para>
/// What differs per provider is configuration, not code: the model id, the endpoint, and
/// whether the provider can be handed the PDF or has to be sent text extracted from it.
/// </para>
/// <para>
/// There are no retries and no timeout here. <see cref="GuardedNoticeExtractor"/> wraps every
/// registration and owns both, so a provider cannot forget to bound its own call and cannot
/// invent a second retry policy underneath the one that says there are none.
/// </para>
/// </remarks>
public sealed class ChatClientNoticeExtractor(
    IChatClient chatClient,
    ProviderOptions provider,
    IPdfTextExtractor? pdfTextExtractor = null) : INoticeExtractor
{
    private readonly IChatClient _chatClient = chatClient
        ?? throw new ArgumentNullException(nameof(chatClient));

    private readonly ProviderOptions _provider = provider
        ?? throw new ArgumentNullException(nameof(provider));

    public string ModelName => _provider.ModelId;

    public async Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        var prompt = PromptCatalog.Extraction;
        var started = Stopwatch.GetTimestamp();

        // Before the call, so an oversized notice fails saying it is oversized. The vendor's
        // own answer to a too-large request is a transport error that reads like an outage.
        if (pdfBytes.Length > _provider.MaxDocumentBytes)
        {
            return ExtractionResult.ProviderFailure(
                ModelName,
                prompt.Version,
                Since(started),
                $"The notice is {pdfBytes.Length:N0} bytes and {ModelName} accepts "
                + $"{_provider.MaxDocumentBytes:N0}. Split it or route it to a provider that takes it.");
        }

        var message = BuildMessage(pdfBytes, prompt);
        var response = await _chatClient
            .GetResponseAsync([message], BuildOptions(prompt), cancellationToken)
            .ConfigureAwait(false);

        return Interpret(response, prompt, Telemetry(response, started));
    }

    /// <summary>
    /// Tokens, latency and finish reason, recorded whatever the outcome.
    /// </summary>
    /// <remarks>
    /// Token counts are what makes cost per notice a number rather than a monthly surprise,
    /// and they are recorded on failures too — a run that failed still cost what it cost, and
    /// a provider that burns tokens producing nothing is exactly the thing worth spotting.
    /// The counts are longs upstream; anything that overflows an int here is a bug worth
    /// seeing rather than a value worth silently wrapping.
    /// </remarks>
    private static ExtractionTelemetry Telemetry(ChatResponse response, long started) =>
        new(
            PromptTokens: Clamp(response.Usage?.InputTokenCount),
            CompletionTokens: Clamp(response.Usage?.OutputTokenCount),
            Latency: Stopwatch.GetElapsedTime(started),
            FinishReason: response.FinishReason?.Value);

    private static int? Clamp(long? count) =>
        count is null ? null : (int)Math.Min(count.Value, int.MaxValue);

    /// <summary>
    /// The document, native where the provider takes it and flattened where it does not.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContent"/> carries the bytes and a media type through the abstraction,
    /// so a PDF reaches Claude as a document rather than as something a preprocessing step
    /// guessed at. The text branch exists for Groq, whose hosted models do not take documents.
    /// </remarks>
    private ChatMessage BuildMessage(byte[] pdfBytes, ExtractionPrompt prompt)
    {
        if (_provider.SendsPdfNatively)
        {
            return new ChatMessage(ChatRole.User,
            [
                new TextContent(prompt.Text),
                new DataContent(pdfBytes, "application/pdf"),
            ]);
        }

        if (pdfTextExtractor is null)
        {
            throw new ExtractionProviderException(
                $"{ModelName} cannot be sent a PDF and no text extractor is registered. "
                + "Either register one or configure this provider as taking documents natively.");
        }

        var text = pdfTextExtractor.Extract(pdfBytes);

        return new ChatMessage(ChatRole.User,
        [
            new TextContent(prompt.Text),
            new TextContent($"\n\n--- NOTICE TEXT ---\n{text}"),
        ]);
    }

    /// <summary>
    /// The schema goes to the provider, not just the prompt.
    /// </summary>
    /// <remarks>
    /// This is the second of the three layers keeping bank details out of the database, and
    /// the only one the model cannot talk its way past — every object in the schema is
    /// <c>additionalProperties: false</c>, so there is no field for an account number to
    /// arrive in. Sending it as an instruction only would demote that layer to advice.
    /// </remarks>
    private ChatOptions BuildOptions(ExtractionPrompt prompt)
    {
        using var schema = JsonDocument.Parse(prompt.JsonSchema);

        return new ChatOptions
        {
            ModelId = _provider.ModelId,
            MaxOutputTokens = _provider.MaxTokens,

            // Zero rather than left to the vendor default. Two runs of the same notice through
            // the same model should differ because the model changed, not because sampling did
            // — otherwise the accuracy report measures noise.
            Temperature = 0f,

            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                schema.RootElement.Clone(),
                schemaName: "notice_extraction",
                schemaDescription: "Economic events extracted from a syndicated-loan notice."),
        };
    }

    private ExtractionResult Interpret(ChatResponse response, ExtractionPrompt prompt, ExtractionTelemetry telemetry)
    {
        var raw = response.Text ?? string.Empty;

        // A truncated answer is a failure, not a partial success. It parses as far as it goes
        // and the missing tail looks exactly like fields the notice did not state, which is the
        // one failure this system must never present as a clean extraction.
        if (response.FinishReason == ChatFinishReason.Length)
        {
            return ExtractionResult.ParseFailure(
                ModelName,
                prompt.Version,
                raw,
                telemetry,
                $"The response hit the {_provider.MaxTokens:N0} token cap and stopped mid-answer. "
                + "Treated as a failure: the part that arrived is indistinguishable from a notice "
                + "that stated fewer fields.");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return ExtractionResult.ParseFailure(
                ModelName, prompt.Version, raw, telemetry,
                $"{ModelName} returned no content. Finish reason: {Describe(response.FinishReason)}.");
        }

        try
        {
            var fields = ExtractedFieldFlattener.Flatten(raw)
                .ToDictionary(field => field.FieldName, field => field.RawValue, StringComparer.Ordinal);

            return ExtractionResult.Success(ModelName, prompt.Version, raw, fields, telemetry);
        }
        catch (JsonException ex)
        {
            // The body is kept. It is the evidence a reviewer needs and the only way to tell a
            // model that answered badly from one that did not answer.
            return ExtractionResult.ParseFailure(ModelName, prompt.Version, raw, telemetry, ex.Message);
        }
    }

    private static ExtractionTelemetry Since(long started) =>
        new(Latency: Stopwatch.GetElapsedTime(started));

    private static string Describe(ChatFinishReason? reason) => reason?.Value ?? "not stated";
}
