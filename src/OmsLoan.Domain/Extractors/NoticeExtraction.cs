using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// The only way to run an extraction, anywhere in the system.
/// </summary>
/// <remarks>
/// <para>
/// Per #70. Before this existed there were four routes to an extraction and only one of them
/// was safe: the selector, a keyed resolve out of the container, and direct construction of
/// either the provider or its guard. Direct construction was the dangerous one — an extractor
/// built that way has no <c>GuardedNoticeExtractor</c> around it, so it silently loses the
/// one-attempt rule, the deadline, and the promise that every failure becomes a recorded row
/// rather than an exception escaping into the ingestion loop. It compiled, it read naturally,
/// and nothing about the call site looked wrong.
/// </para>
/// <para>
/// Everything behind this interface is now internal, so a caller has exactly one option
/// because the others do not compile. That is also what makes any cross-cutting rule cheap:
/// the concurrency limit below is five lines here instead of a convention every call site has
/// to remember, and cost accounting or a circuit breaker would go the same way.
/// </para>
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
    /// The classifier's hint, or <see cref="NoticeType.Unknown"/> when there is none. A hint and
    /// not an instruction: the extractor is allowed to disagree.
    /// </param>
    /// <param name="providerName">
    /// Which provider, or null for the configured default. Named explicitly by reprocessing and
    /// by the accuracy report, which exist to run the same notice through two of them.
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
    /// <para>
    /// Per provider rather than one global gate, because rate limits and latency are per
    /// vendor. A single shared limit would let one slow Claude call block a Groq call that
    /// shares nothing with it — the providers are independent and the limit should be too.
    /// </para>
    /// <para>
    /// <see cref="SemaphoreSlim"/> and not <c>lock</c>: a lock cannot be held across an
    /// <c>await</c>, which rules out the obvious first attempt. It also matters that the
    /// waiting here is asynchronous — an awaiting call holds no thread, so two extractions in
    /// flight with a queue behind them costs approximately no threads at all. The limit is on
    /// concurrent <em>work</em>, which is what meets a rate limit and spends money, not on
    /// threads, which were never the scarce thing.
    /// </para>
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

        // Throws naming what is configured rather than falling back. A reprocess run that asked
        // for one provider and silently got another produces rows labelled with a model that
        // never saw the notice, which is worse than not running.
        var extractor = _selector.Get(providerName);
        var resolved = string.IsNullOrWhiteSpace(providerName) ? _options.DefaultProvider : providerName;

        var gate = _gates.GetOrAdd(resolved, CreateGate);
        var queued = Stopwatch.GetTimestamp();

        // The token is passed so a queued extraction does not outlive shutdown. The Worker gets
        // twenty seconds from the SCM; a wait that ignored cancellation would be a service
        // killed and logged as a crash.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var waited = Stopwatch.GetElapsedTime(queued);

        try
        {
            var result = await extractor
                .ExtractAsync(pdfBytes, noticeType, cancellationToken)
                .ConfigureAwait(false);

            // Recorded separately from provider latency. Without the split, being behind our own
            // limit is indistinguishable from a slow vendor — and one of those is a capacity
            // decision we control and the other is not.
            return result with { Telemetry = result.Telemetry with { QueueWait = waited } };
        }
        finally
        {
            // The one genuinely load-bearing finally in this codebase. A permit leaked on a
            // failure path permanently reduces capacity, and the symptom surfaces much later as
            // "extraction got slow" with nothing in the logs pointing at the cause.
            gate.Release();
        }
    }

    /// <summary>
    /// One gate for a provider, with the configured value checked rather than trusted.
    /// </summary>
    /// <remarks>
    /// A cap of zero or less is a deployment mistake, and left to <see cref="SemaphoreSlim"/> it
    /// surfaces as an <c>ArgumentOutOfRangeException</c> thrown from here — outside the guard, so
    /// it escapes as an exception rather than becoming a recorded failure, and the message names
    /// a parameter nobody configured. Worse, a cap of zero would otherwise mean "block forever",
    /// which looks like a hung provider rather than a typo.
    ///
    /// So it is refused by name, saying which provider and which setting.
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
