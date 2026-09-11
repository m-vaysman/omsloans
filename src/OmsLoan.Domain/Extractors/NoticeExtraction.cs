using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// The only way to run an extraction anywhere in the system.
/// </summary>
/// <remarks>
/// Per #70. Before this, four routes existed and only one was safe. Direct construction of a
/// provider skipped <see cref="GuardedNoticeExtractor"/> — losing the one-attempt rule, the
/// deadline, and the promise that every failure becomes a recorded row. It compiled and looked fine.
///
/// Everything behind this interface is internal, so the other routes do not compile. Cross-cutting
/// rules (concurrency here; cost accounting later) live in one place.
/// </remarks>
public interface INoticeExtraction
{
    /// <summary>Providers that are both configured and registered, in configuration order.</summary>
    IReadOnlyList<string> AvailableProviders { get; }

    /// <summary>
    /// Reads one notice. Never throws for a provider failure — the outcome is in the result.
    /// </summary>
    /// <param name="pdfBytes">The notice, verbatim.</param>
    /// <param name="noticeType">
    /// Classifier hint, or <see cref="NoticeType.Unknown"/> when there is none. A hint, not an
    /// instruction: the extractor may disagree.
    /// </param>
    /// <param name="providerName">
    /// Which provider, or null for the configured default. Named explicitly by reprocessing and
    /// by the accuracy report, which run the same notice through two providers.
    /// </param>
    Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType = NoticeType.Unknown,
        string? providerName = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="INoticeExtraction"/>
public sealed class NoticeExtraction : INoticeExtraction
{
    private readonly INoticeExtractorSelector _selector;
    private readonly ExtractionOptions _options;

    /// <summary>
    /// One gate per provider, created on first use.
    /// </summary>
    /// <remarks>
    /// Per provider, not one global gate: rate limits and latency are per vendor. A shared
    /// limit would let a slow Claude call block a Groq call that shares nothing with it.
    ///
    /// <see cref="SemaphoreSlim"/>, not <c>lock</c>: a lock cannot be held across <c>await</c>.
    /// Waiting is asynchronous — an awaiting call holds no thread. The limit is concurrent work
    /// (rate limit, spend, PDFs in memory), not threads.
    /// </remarks>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    internal NoticeExtraction(INoticeExtractorSelector selector, IOptions<ExtractionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(options);

        _selector = selector;
        _options = options.Value;
    }

    public IReadOnlyList<string> AvailableProviders => _selector.Available;

    public async Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType = NoticeType.Unknown,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        // Throws naming what is configured rather than falling back. A reprocess that asked for
        // one provider and silently got another labels rows with a model that never saw the notice.
        var extractor = _selector.Get(providerName);
        var resolved = string.IsNullOrWhiteSpace(providerName) ? _options.DefaultProvider : providerName;

        var gate = _gates.GetOrAdd(resolved, CreateGate);
        var queued = Stopwatch.GetTimestamp();

        // Token so a queued extraction does not outlive shutdown. The Worker gets twenty seconds
        // from Windows Service Control Manager; a wait that ignored cancellation would be a
        // service killed and logged as a crash.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var waited = Stopwatch.GetElapsedTime(queued);

        try
        {
            var result = await extractor
                .ExtractAsync(pdfBytes, noticeType, cancellationToken)
                .ConfigureAwait(false);

            // Separate from provider latency. Without the split, our own queue looks like a slow vendor.
            return result with { Telemetry = result.Telemetry with { QueueWait = waited } };
        }
        finally
        {
            // Load-bearing. A permit leaked on failure permanently reduces capacity; the symptom
            // shows up later as "extraction got slow" with nothing in the logs pointing here.
            gate.Release();
        }
    }

    /// <summary>
    /// One gate for a provider; the configured cap is checked rather than trusted.
    /// </summary>
    /// <remarks>
    /// A cap below one is a deployment mistake. Left to <see cref="SemaphoreSlim"/> it throws
    /// <c>ArgumentOutOfRangeException</c> outside the guard — an escaping exception instead of a
    /// recorded failure, naming a parameter nobody configured. Zero would block every extraction
    /// against that provider forever and read as a hung vendor rather than a typo. Refused by name.
    /// </remarks>
    private SemaphoreSlim CreateGate(string providerName)
    {
        var configured = _options.Providers.TryGetValue(providerName, out var provider)
            ? provider.MaxConcurrentExtractions
            : ProviderOptions.DefaultMaxConcurrentExtractions;

        if (configured < 1)
        {
            throw new InvalidOperationException(
                $"Extraction:Providers:{providerName}:MaxConcurrentExtractions is {configured}. "
                + "It must be at least 1 — zero would block every extraction against this "
                + "provider forever, which reads as a hung provider rather than a setting.");
        }

        return new SemaphoreSlim(configured, configured);
    }
}
