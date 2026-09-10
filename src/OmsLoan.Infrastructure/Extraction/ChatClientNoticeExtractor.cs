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
/// <strong>Three steps, deliberately separable.</strong> <see cref="Preflight"/> rejects what
/// can be rejected without asking anyone. <see cref="BuildRequest"/> assembles the whole call
/// and throws rather than returning something partial. Only then does anything reach the
/// network. Everything before the send is pure, so it can be tested exhaustively for free —
/// and a request that was built successfully is one where the only remaining risk is
/// transport.
/// </para>
/// <para>
/// There are no retries and no timeout here. <see cref="GuardedNoticeExtractor"/> wraps every
/// registration and owns both, so a provider cannot forget to bound its own call and cannot
/// invent a second retry policy underneath the one that says there are none.
/// </para>
/// </remarks>
internal sealed class ChatClientNoticeExtractor(
    IChatClient chatClient,
    ProviderOptions provider,
    IPdfTextExtractor? pdfTextExtractor = null) : INoticeExtractor
{
    private readonly IChatClient _chatClient = chatClient
        ?? throw new ArgumentNullException(nameof(chatClient));

    private readonly ProviderOptions _provider = provider
        ?? throw new ArgumentNullException(nameof(provider));

    public string ModelName => _provider.ModelId;

    /// <summary>The mode this provider will use, before any document is seen.</summary>
    private DocumentMode Mode =>
        _provider.SendsPdfNatively ? DocumentMode.Native : DocumentMode.ExtractedText;

    public async Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        var started = Stopwatch.GetTimestamp();
        var prompt = PromptCatalog.Extraction;

        // Everything that can be known without asking the provider is settled first, so a
        // rejectable notice costs nothing.
        var refusal = Preflight(pdfBytes);

        if (refusal is not null)
        {
            return ExtractionResult.ProviderFailure(ModelName, prompt.Version, Telemetry(started), refusal);
        }

        var request = BuildRequest(pdfBytes);

        var response = await _chatClient
            .GetResponseAsync([request.Message], request.Options, cancellationToken)
            .ConfigureAwait(false);

        return Interpret(response, request, Telemetry(started, response));
    }

    /// <summary>
    /// What can be refused without a call. Returns the reason, or null to proceed.
    /// </summary>
    /// <remarks>
    /// Cheap failures happen here so they cost nothing and say what is wrong. An oversized
    /// notice sent anyway comes back as whatever the vendor returns for a too-large request,
    /// which is a generic transport error that reads like an outage and sends whoever sees it
    /// looking in the wrong place.
    /// </remarks>
    public string? Preflight(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        if (pdfBytes.Length == 0)
        {
            return "The notice is empty. Nothing was sent.";
        }

        if (pdfBytes.Length > _provider.MaxDocumentBytes)
        {
            return $"The notice is {pdfBytes.Length:N0} bytes and {ModelName} accepts "
                + $"{_provider.MaxDocumentBytes:N0}. Split it or route it to a provider that takes it.";
        }

        if (Mode == DocumentMode.ExtractedText && pdfTextExtractor is null)
        {
            return $"{ModelName} cannot be sent a PDF and no text extractor is registered. "
                + "Either register one or configure this provider as taking documents natively.";
        }

        return null;
    }

    /// <summary>
    /// Assembles the whole call. Complete or it throws — never partial.
    /// </summary>
    /// <remarks>
    /// Public so it can be asserted on directly. The costly questions about a provider are
    /// whether it honours a schema and whether a PDF survives to it, and those need a real
    /// call; everything else about a request — that the document is attached with the right
    /// media type, that the schema went in the options rather than only into the prose, that
    /// the model id came from configuration — is settled here, for free.
    /// </remarks>
    public ExtractionRequest BuildRequest(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        var prompt = PromptCatalog.Extraction;

        return ExtractionRequest.Build(
            prompt,
            BuildContent(pdfBytes, prompt),
            BuildOptions(prompt),
            Mode);
    }

    private IList<AIContent> BuildContent(byte[] pdfBytes, ExtractionPrompt prompt)
    {
        if (Mode == DocumentMode.Native)
        {
            return [new TextContent(prompt.Text), new DataContent(pdfBytes, "application/pdf")];
        }

        var text = (pdfTextExtractor ?? throw new ExtractionProviderException(
                $"{ModelName} cannot be sent a PDF and no text extractor is registered."))
            .Extract(pdfBytes);

        return [new TextContent(prompt.Text), new TextContent($"\n\n--- NOTICE TEXT ---\n{text}")];
    }

    /// <summary>
    /// The schema goes to the provider, not just into the prompt.
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

    private ExtractionResult Interpret(ChatResponse response, ExtractionRequest request, ExtractionTelemetry telemetry)
    {
        var raw = response.Text ?? string.Empty;
        var version = request.Prompt.Version;

        // A truncated answer is a failure, not a partial success. It parses as far as it goes
        // and the missing tail looks exactly like fields the notice did not state, which is the
        // one failure this system must never present as a clean extraction.
        if (response.FinishReason == ChatFinishReason.Length)
        {
            return ExtractionResult.ParseFailure(
                ModelName, version, raw, telemetry,
                $"The response hit the {_provider.MaxTokens:N0} token cap and stopped mid-answer. "
                + "Treated as a failure: the part that arrived is indistinguishable from a notice "
                + "that stated fewer fields.");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return ExtractionResult.ParseFailure(
                ModelName, version, raw, telemetry,
                $"{ModelName} returned no content. Finish reason: {response.FinishReason?.Value ?? "not stated"}.");
        }

        try
        {
            var fields = ExtractedFieldFlattener.Flatten(raw)
                .ToDictionary(field => field.FieldName, field => field.RawValue, StringComparer.Ordinal);

            return ExtractionResult.Success(ModelName, version, raw, fields, telemetry);
        }
        catch (JsonException ex)
        {
            // The body is kept. It is the evidence a reviewer needs and the only way to tell a
            // model that answered badly from one that did not answer.
            return ExtractionResult.ParseFailure(ModelName, version, raw, telemetry, ex.Message);
        }
    }

    /// <summary>
    /// Latency, tokens, finish reason, and how the notice was sent — on every outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded on failures as well as successes. A run that failed still cost what it cost,
    /// and a provider burning tokens to produce nothing is exactly the thing worth spotting.
    /// </para>
    /// <para>
    /// <see cref="DocumentMode"/> is stamped here rather than left in configuration, because
    /// configuration says what a provider does now and an accuracy report asks what a
    /// particular run did. Comparing a native read against a flattened one without knowing
    /// which was which blames the model for a preprocessing loss.
    /// </para>
    /// </remarks>
    private ExtractionTelemetry Telemetry(long started, ChatResponse? response = null) =>
        new(
            PromptTokens: Tokens(response?.Usage?.InputTokenCount),
            CompletionTokens: Tokens(response?.Usage?.OutputTokenCount),
            Latency: Stopwatch.GetElapsedTime(started),
            FinishReason: response?.FinishReason?.Value,
            DocumentMode: Mode);

    /// <summary>
    /// Token counts are longs upstream and an int here, because that is what the column holds.
    /// </summary>
    /// <remarks>
    /// A count past <see cref="int.MaxValue"/> is not a large notice, it is a broken provider
    /// response — two billion tokens is several thousand times any real context window. So it
    /// is dropped rather than saturated: null reads as "not reported", which is true and
    /// visibly odd, where <c>int.MaxValue</c> would read as a real measurement and quietly
    /// wreck any cost total it was summed into.
    /// </remarks>
    private static int? Tokens(long? count) =>
        count is null or < 0 or > int.MaxValue ? null : (int)count.Value;
}
