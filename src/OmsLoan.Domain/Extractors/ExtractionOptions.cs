namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Settings for one provider.
/// </summary>
/// <remarks>
/// The same shape for every vendor. Nothing here is Claude-specific or OpenAI-specific,
/// because the moment one provider needs a setting the others do not, the abstraction has a
/// leak in it and the reprocess command has a special case.
/// </remarks>
public sealed class ProviderOptions
{
    /// <summary>The key. Empty means the provider is unconfigured and will not be registered.</summary>
    /// <remarks>
    /// Supplied by a flat machine variable — <c>CLAUDE_API_KEY</c> and its siblings — and never
    /// committed. See docs/windows-service.md.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// The pinned model id recorded on every row this provider produces.
    /// </summary>
    /// <remarks>
    /// Pinned, not a floating alias. Two rows carrying the same name and different behaviour
    /// would make the accuracy report meaningless and nothing afterwards could separate them.
    /// </remarks>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Cap on the response. A notice that needs more than this is a prompt problem.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// How long the call may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// The whole budget for one extraction, because there is only ever one attempt. A
    /// provider that hangs must not hold an ingestion pass open behind it: the notice is
    /// still on disk or in the mailbox, and a failed extraction a reviewer can see is a far
    /// better state than a Worker stuck on one document.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 120;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ModelId);
}

/// <summary>
/// The <c>Extraction</c> configuration section: which provider to use, and the settings for
/// each.
/// </summary>
/// <remarks>
/// Providers are keyed by name — <c>Claude</c>, <c>OpenAi</c>, <c>Groq</c> — so that choosing
/// one is a configuration value rather than a code path, which is what ADR 0001 is about and
/// what the reprocess command needs in order to run the same notice through two of them.
/// </remarks>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>
    /// Which provider is used when a caller does not name one.
    /// </summary>
    /// <remarks>
    /// Claude by default because it accepts PDFs natively, and these documents are tabular —
    /// a rate reset flattened to text loses which rate belongs to which tranche. See ADR 0001.
    /// </remarks>
    public string DefaultProvider { get; init; } = "Claude";

    /// <summary>Per-provider settings, keyed by provider name.</summary>
    public Dictionary<string, ProviderOptions> Providers { get; init; } = [];

    /// <summary>Provider names that have both a key and a model id.</summary>
    public IReadOnlyList<string> ConfiguredProviders =>
        [.. Providers.Where(p => p.Value.IsConfigured).Select(p => p.Key)];
}
