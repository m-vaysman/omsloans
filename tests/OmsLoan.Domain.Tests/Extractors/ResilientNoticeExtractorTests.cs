using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The resilience decorator: what it retries, what it refuses to retry, and the promise that
/// a provider problem comes back as a row rather than an exception.
/// </summary>
public class ResilientNoticeExtractorTests
{
    private readonly List<TimeSpan> _delays = [];

    private ResilientNoticeExtractor Wrap(
        FakeNoticeExtractor inner,
        int maxAttempts = 3,
        int timeoutSeconds = 120,
        int baseDelayMs = 1000) =>
        new(inner,
            new ProviderOptions
            {
                ApiKey = "k",
                ModelId = inner.ModelName,
                MaxAttempts = maxAttempts,
                TimeoutSeconds = timeoutSeconds,
                RetryBaseDelayMilliseconds = baseDelayMs,
            },
            // Delays are recorded rather than waited on: a test that actually sleeps for the
            // backoff schedule is a test nobody runs.
            delay: (wait, _) => { _delays.Add(wait); return Task.CompletedTask; },
            // Jitter pinned to its ceiling so the schedule can be asserted exactly instead of
            // as a range.
            jitter: () => 1.0);

    private static ExtractionProviderException Transient(int status = 429) =>
        ExtractionProviderException.FromStatusCode(status, "Too many requests.");

    private static ExtractionProviderException Permanent(int status = 401) =>
        ExtractionProviderException.FromStatusCode(status, "Invalid API key.");

    [Fact]
    public async Task ASuccessPassesThroughWithLatencyAndAttemptCount()
    {
        var inner = new FakeNoticeExtractor();

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Telemetry.Attempts);
        Assert.True(result.Telemetry.Latency >= TimeSpan.Zero);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task ATransientFailureIsRetriedAndCanSucceed()
    {
        var inner = new FakeNoticeExtractor().Throws(Transient()).Throws(Transient(503));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(3, result.Telemetry.Attempts);
    }

    [Fact]
    public async Task TransientFailuresThatOutlastTheAttemptsBecomeAProviderFailure()
    {
        var inner = new FakeNoticeExtractor()
            .Throws(Transient()).Throws(Transient()).Throws(Transient());

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(3, result.Telemetry.Attempts);
        Assert.Contains("429", result.Error);
    }

    /// <summary>
    /// The distinction that earns the exception type. Retrying a bad key three times with
    /// backoff turns a five-second failure into a thirty-second one, calls the provider three
    /// times to be told the same thing, and delays the log entry that says what is wrong.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    public async Task APermanentFailureIsNotRetried(int status)
    {
        var inner = new FakeNoticeExtractor().Throws(Permanent(status));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(_delays);
        Assert.Contains(status.ToString(), result.Error);
    }

    /// <summary>
    /// 408 is the exception among the 4xx: the server saying it gave up waiting, not that the
    /// request was wrong.
    /// </summary>
    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    public void TheTransientStatusesAreTheOnesWorthRetrying(int status, bool expected) =>
        Assert.Equal(expected, ExtractionProviderException.IsTransientStatus(status));

    /// <summary>
    /// Criterion 5, and the one that protects the Worker: a provider that never answers must
    /// not hold the ingestion loop open. The notice is still on disk or in the mailbox.
    /// </summary>
    [Fact]
    public async Task AHungProviderIsAbandonedAndReportedAsATimeout()
    {
        var inner = new FakeNoticeExtractor().Hangs().Hangs().Hangs();

        var result = await Wrap(inner, timeoutSeconds: 0).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.TimedOut, result.Outcome);
        Assert.Equal(3, inner.Calls);
        Assert.Contains("No response", result.Error);
    }

    /// <summary>
    /// Shutdown is not a failed extraction. Nothing is recorded and nobody is told, because
    /// the notice is untouched and will be picked up again.
    /// </summary>
    [Fact]
    public async Task CallerCancellationPropagatesRatherThanBecomingAResult()
    {
        var inner = new FakeNoticeExtractor().Hangs();
        using var shutdown = new CancellationTokenSource();
        shutdown.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Wrap(inner).ExtractAsync([1], NoticeType.Unknown, shutdown.Token));
    }

    /// <summary>
    /// Backoff doubles. With jitter pinned to its ceiling the schedule is exact; in production
    /// each wait is a uniform pick below these, so a batch that failed together does not retry
    /// together and re-create the burst.
    /// </summary>
    [Fact]
    public async Task BackoffIsExponential()
    {
        var inner = new FakeNoticeExtractor()
            .Throws(Transient()).Throws(Transient()).Throws(Transient()).Throws(Transient());

        await Wrap(inner, maxAttempts: 4, baseDelayMs: 100).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)],
            _delays);
    }

    /// <summary>
    /// A response that will not parse is not a failure to retry — the provider answered, and
    /// the answer is the evidence. It passes straight through, raw response intact.
    /// </summary>
    [Fact]
    public async Task AParseFailurePassesThroughWithItsRawResponseKept()
    {
        var inner = new FakeNoticeExtractor().Returns(
            ExtractionResult.ParseFailure("fake-model-1", "v3", "{ truncated", new ExtractionTelemetry(), "Unexpected end of JSON."));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ParseFailed, result.Outcome);
        Assert.Equal("{ truncated", result.RawJson);
        Assert.Equal("v3", result.PromptVersion);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Telemetry the provider reported survives the decorator, which only fills in what it
    /// alone knows: how long the whole thing took and how many attempts it needed.
    /// </summary>
    [Fact]
    public async Task ProviderTelemetryIsPreservedAndTheDecoratorAddsItsOwn()
    {
        var inner = new FakeNoticeExtractor()
            .Throws(Transient())
            .Returns(ExtractionResult.Success(
                "fake-model-1",
                "v2",
                "{}",
                new Dictionary<string, string?> { ["rate"] = "5.25" },
                new ExtractionTelemetry(PromptTokens: 1200, CompletionTokens: 300, FinishReason: "stop")));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(1200, result.Telemetry.PromptTokens);
        Assert.Equal(300, result.Telemetry.CompletionTokens);
        Assert.Equal(1500, result.Telemetry.TotalTokens);
        Assert.Equal("stop", result.Telemetry.FinishReason);
        Assert.Equal(2, result.Telemetry.Attempts);
        Assert.Equal("5.25", result.Fields["rate"]);
    }
}
