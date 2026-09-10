using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The single entry point, and the concurrency gate that can only live in it.
/// </summary>
/// <remarks>
/// Built through the real registration rather than by hand, because the thing being asserted is
/// the wiring: that the gate is in the path every caller takes, and that no caller can opt out
/// of it. A hand-constructed <see cref="NoticeExtraction"/> would prove the class works and
/// nothing about whether the system uses it.
/// </remarks>
public class NoticeExtractionTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 pretend"u8.ToArray();

    private static (INoticeExtraction Extraction, CountingExtractor Inner) Build(
        int maxConcurrent = 2,
        Func<CountingExtractor, CountingExtractor>? arrange = null)
    {
        var options = new ExtractionOptions { DefaultProvider = "Claude" };
        options.Providers["Claude"] = new ProviderOptions
        {
            ApiKey = "k",
            ModelId = "claude-test",
            MaxConcurrentExtractions = maxConcurrent,
        };

        var inner = arrange?.Invoke(new CountingExtractor()) ?? new CountingExtractor();

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddNoticeExtraction();
        services.AddNoticeExtractor(options, "Claude", (_, _) => inner);

        return (services.BuildServiceProvider().GetRequiredService<INoticeExtraction>(), inner);
    }

    // --- the gate -----------------------------------------------------------------------------

    /// <summary>
    /// At most two extractions in flight, whatever the caller does.
    /// </summary>
    /// <remarks>
    /// Ten launched at once against a cap of two. The fake records the high-water mark of
    /// concurrent calls, which is the only number that matters — a limit that held on average
    /// and not at the peak would still meet a rate limit at the peak.
    /// </remarks>
    [Fact]
    public async Task NoMoreThanTheConfiguredNumberRunAtOnce()
    {
        var (extraction, inner) = Build(maxConcurrent: 2, arrange: e => e.Delays(TimeSpan.FromMilliseconds(40)));

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default)));

        Assert.Equal(10, inner.Calls);
        Assert.True(inner.PeakConcurrent <= 2, $"Peaked at {inner.PeakConcurrent} concurrent extractions.");
    }

    [Fact]
    public async Task ACapOfOneSerialisesCompletely()
    {
        var (extraction, inner) = Build(maxConcurrent: 1, arrange: e => e.Delays(TimeSpan.FromMilliseconds(20)));

        await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default)));

        Assert.Equal(1, inner.PeakConcurrent);
    }

    /// <summary>
    /// The load-bearing <c>finally</c>, stated as a test.
    /// </summary>
    /// <remarks>
    /// A permit leaked on a failure path permanently reduces capacity, and the symptom appears
    /// much later as unexplained slowness with nothing in the logs. So: run far more failing
    /// extractions than there are permits, and then assert a normal one still gets through. If
    /// permits leaked, the cap would have reached zero and this would hang rather than fail.
    /// </remarks>
    [Fact]
    public async Task AFailingExtractionDoesNotLeakItsPermit()
    {
        var (extraction, _) = Build(maxConcurrent: 2, arrange: e => e.Fails());

        for (var i = 0; i < 8; i++)
        {
            var failed = await extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default);

            Assert.Equal(ExtractionOutcome.ProviderFailed, failed.Outcome);
        }

        // Capacity survived. The token bounds it, so a leaked permit fails this as a cancelled
        // wait with an obvious cause rather than hanging the suite for someone to diagnose.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var survived = await extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: bound.Token);

        Assert.Equal(ExtractionOutcome.ProviderFailed, survived.Outcome);
    }

    /// <summary>
    /// Waiting for a permit honours cancellation, so a queued extraction cannot outlive
    /// shutdown. The Worker gets twenty seconds from the SCM; a wait that ignored the token
    /// would be a service killed and logged as a crash.
    /// </summary>
    [Fact]
    public async Task AQueuedExtractionIsCancellable()
    {
        var (extraction, _) = Build(maxConcurrent: 1, arrange: e => e.Delays(TimeSpan.FromSeconds(5)));

        // Occupies the only permit.
        var blocking = extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default);

        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: shutdown.Token));

        await blocking;
    }

    /// <summary>
    /// Time spent queued is recorded apart from provider latency. Without the split, being
    /// behind our own limit is indistinguishable from a slow vendor — and only one of those is
    /// a capacity decision we control.
    /// </summary>
    [Fact]
    public async Task TimeSpentQueuedIsRecordedSeparatelyFromLatency()
    {
        var (extraction, _) = Build(maxConcurrent: 1, arrange: e => e.Delays(TimeSpan.FromMilliseconds(120)));

        var first = extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default);
        var second = extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default);

        var results = await Task.WhenAll(first, second);

        // One waited for the other to finish; the other went straight through. Compared against
        // a threshold rather than against zero, because even an uncontended acquire measures a
        // few ticks — "queued" means a wait worth reporting, not a wait of exactly nothing.
        var threshold = TimeSpan.FromMilliseconds(50);

        Assert.True(
            results.Max(r => r.Telemetry.QueueWait) > threshold,
            "Neither extraction recorded having queued, though one had to wait for the other.");

        Assert.True(
            results.Min(r => r.Telemetry.QueueWait) < threshold,
            "Both extractions recorded queueing, though only one could have waited.");
    }

    // --- routing and resolution ----------------------------------------------------------------

    [Fact]
    public async Task TheDefaultProviderIsUsedWhenNoneIsNamed()
    {
        var (extraction, inner) = Build();

        var result = await extraction.ExtractAsync(Pdf, NoticeType.Unknown, cancellationToken: default);

        Assert.Equal("claude-test", result.ModelName);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Throws naming what is configured rather than falling back. A reprocess run that asked for
    /// one provider and silently got another produces rows labelled with a model that never saw
    /// the notice, which is worse than not running at all.
    /// </summary>
    [Fact]
    public async Task AnUnknownProviderThrowsNamingWhatIsConfigured()
    {
        var (extraction, _) = Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => extraction.ExtractAsync(Pdf, NoticeType.Unknown, "Gemini"));

        Assert.Contains("Gemini", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Claude", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailableProvidersReportsWhatIsRegistered()
    {
        var (extraction, _) = Build();

        Assert.Equal(["Claude"], extraction.AvailableProviders);
    }

    /// <summary>
    /// The hint reaches the provider. It is a hint and not an instruction — #68 lets the
    /// extractor disagree — but it has to arrive for that to mean anything.
    /// </summary>
    [Fact]
    public async Task TheClassifierHintIsPassedThrough()
    {
        var (extraction, inner) = Build();

        await extraction.ExtractAsync(Pdf, NoticeType.RateReset, cancellationToken: default);

        Assert.Equal(NoticeType.RateReset, inner.LastNoticeType);
    }

    /// <summary>
    /// An extractor that records concurrency, because the cap is the thing under test.
    /// </summary>
    private sealed class CountingExtractor : INoticeExtractor
    {
        private int _current;
        private TimeSpan _delay;
        private bool _fails;

        public string ModelName => "claude-test";

        public int Calls { get; private set; }

        public int PeakConcurrent { get; private set; }

        public NoticeType LastNoticeType { get; private set; }

        public CountingExtractor Delays(TimeSpan delay)
        {
            _delay = delay;
            return this;
        }

        public CountingExtractor Fails()
        {
            _fails = true;
            return this;
        }

        public async Task<ExtractionResult> ExtractAsync(
            byte[] pdfBytes,
            NoticeType noticeType,
            CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _current);

            lock (this)
            {
                Calls++;
                LastNoticeType = noticeType;
                if (now > PeakConcurrent) PeakConcurrent = now;
            }

            try
            {
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }

                return _fails
                    ? ExtractionResult.ProviderFailure(ModelName, "extraction.v1", new ExtractionTelemetry(), "nope")
                    : ExtractionResult.Success(
                        ModelName, "extraction.v1", "{}", new Dictionary<string, string?>(), new ExtractionTelemetry());
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
