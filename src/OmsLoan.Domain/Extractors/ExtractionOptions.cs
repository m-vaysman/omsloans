namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Settings for one provider. Same shape for every vendor — a Claude-only setting would leak the abstraction.
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>The key. Empty means unconfigured and not registered.</summary>
    /// <remarks>
    /// Flat machine variable — <c>CLAUDE_API_KEY</c> and siblings — never committed. See docs/windows-service.md.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Pinned model id recorded on every row this provider produces.
    /// </summary>
    /// <remarks>
    /// Pinned, not a floating alias. Same name and different behaviour would make the accuracy report meaningless.
    /// </remarks>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Cap on the response. A notice that needs more is a prompt problem.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// How long the call may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Whole budget for one extraction — there is only ever one attempt. A hung provider must
    /// not hold ingestion open; a failed extraction a reviewer can see beats a Worker stuck on one document.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 120;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>
    /// Endpoint when it is not the vendor default. How Groq is reached: OpenAI-shaped API, different base URL.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Whether the provider takes the PDF itself, or must be sent text extracted from it.
    /// </summary>
    /// <remarks>
    /// Most consequential setting here, and why it is recorded on every row. A native read and a
    /// flattened-text read are not the same measurement — a rate table loses which tranche owns
    /// which rate through text extraction. An accuracy report that compared them without this
    /// stamp would blame the model for a preprocessing loss.
    /// </remarks>
    public bool SendsPdfNatively { get; init; } = true;

    /// <summary>Largest document this provider will accept, in bytes.</summary>
    /// <remarks>
    /// Checked before the call so an oversized notice fails saying so, not as a generic transport error.
    /// </remarks>
    public int MaxDocumentBytes { get; init; } = 30 * 1024 * 1024;

    /// <summary>Default concurrency cap when configuration does not give one.</summary>
    public const int DefaultMaxConcurrentExtractions = 2;

    /// <summary>
    /// How many extractions may be in flight against this provider at once.
    /// </summary>
    /// <remarks>
    /// Per provider, not global — rate limits and latency belong to a vendor.
    /// Unit is concurrent work, not threads. An awaiting call holds no thread.
    /// </remarks>
    public int MaxConcurrentExtractions { get; init; } = DefaultMaxConcurrentExtractions;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ModelId);
}

/// <summary>
/// The <c>Extraction</c> configuration section: default provider and per-provider settings.
/// </summary>
/// <remarks>
/// Providers keyed by name — <c>Claude</c>, <c>OpenAi</c>, <c>Groq</c> — so choosing one is
/// configuration, not a code path (ADR 0001). Reprocess needs that to run one notice through two providers.
/// </remarks>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>
    /// Provider used when a caller does not name one.
    /// </summary>
    /// <remarks>
    /// Claude by default: accepts PDFs natively, and these documents are tabular — a rate reset
    /// flattened to text loses which rate belongs to which tranche. See ADR 0001.
    /// </remarks>
    public string DefaultProvider { get; init; } = "Claude";

    /// <summary>Per-provider settings, keyed by provider name.</summary>
    public Dictionary<string, ProviderOptions> Providers { get; init; } = [];

    /// <summary>Provider names that have both a key and a model id.</summary>
    public IReadOnlyList<string> ConfiguredProviders =>
        [.. Providers.Where(p => p.Value.IsConfigured).Select(p => p.Key)];
}
