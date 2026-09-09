using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The guard: one attempt, a deadline, and a row whatever happens.
/// </summary>
/// <remarks>
/// There are no retries and these tests are the statement of that. A provider is a black box;
/// if it does not answer, that is recorded as plainly as a success and a reviewer decides what
/// to do — run it again, use another provider, or type the values in. Guessing on their behalf
/// with a backoff schedule buys nothing, because the notice is not lost while nobody retries
/// it: it sits in the queue with a visible failure against it.
/// </remarks>
public class GuardedNoticeExtractorTests
{
    private static GuardedNoticeExtractor Wrap(FakeNoticeExtractor inner, int timeoutSeconds = 120) =>
        new(inner,
            new ProviderOptions { ApiKey = "k", ModelId = inner.ModelName, TimeoutSeconds = timeoutSeconds });

    [Fact]
    public async Task ASuccessPassesThroughWithLatencyAttached()
    {
        var inner = new FakeNoticeExtractor();

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Telemetry.Latency >= TimeSpan.Zero);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// The rule, stated once: whatever the provider says, it is asked exactly once.
    /// </summary>
    [Fact]
    public async Task AFailureIsNotRetried()
    {
        var inner = new FakeNoticeExtractor().Throws(new ExtractionProviderException("Too many requests.", 429));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(1, inner.Calls);
        Assert.Contains("429", result.Error);
    }

    /// <summary>
    /// And that holds for the statuses a retry policy would have treated as worth repeating —
    /// there is no longer any such category.
    /// </summary>
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    [InlineData(400)]
    public async Task NoStatusIsTreatedAsWorthRepeating(int status)
    {
        var inner = new FakeNoticeExtractor().Throws(new ExtractionProviderException("failed", status));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(1, inner.Calls);
        Assert.Contains(status.ToString(), result.Error);
    }

    /// <summary>
    /// A provider throwing something it never meant to is a bug in that provider, and a bug
    /// there must not become a notice with no row against it. The guard owns the row; the
    /// provider owns the fault.
    /// </summary>
    [Fact]
    public async Task AnUnexpectedExceptionFromAProviderStillProducesARow()
    {
        var inner = new FakeNoticeExtractor().Throws(new InvalidOperationException("boom"));

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Contains("InvalidOperationException", result.Error);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public async Task ANullReferenceFromAProviderStillProducesARow()
    {
        var inner = new FakeNoticeExtractor().Throws(new NullReferenceException());

        var result = await Wrap(inner).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
    }

    /// <summary>
    /// The one thing the guard still does beyond bookkeeping: a provider that never answers
    /// must not hold the ingestion loop open. The notice is untouched on disk or in the
    /// mailbox, so giving up costs nothing.
    /// </summary>
    [Fact]
    public async Task AHungProviderIsAbandonedAtTheDeadline()
    {
        var inner = new FakeNoticeExtractor().Hangs();

        var result = await Wrap(inner, timeoutSeconds: 0).ExtractAsync([1], NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.TimedOut, result.Outcome);
        Assert.Equal(1, inner.Calls);
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
    /// A response that will not parse is not a provider failure — the provider answered, and
    /// the answer is the evidence a reviewer needs. It passes straight through, kept.
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
    }

    /// <summary>
    /// Telemetry the provider reported survives; the guard fills in only what it alone knows.
    /// </summary>
    [Fact]
    public async Task ProviderTelemetryIsPreservedAndTheGuardAddsLatency()
    {
        var inner = new FakeNoticeExtractor().Returns(
            ExtractionResult.Success(
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
        Assert.Equal("5.25", result.Fields["rate"]);
    }
}
