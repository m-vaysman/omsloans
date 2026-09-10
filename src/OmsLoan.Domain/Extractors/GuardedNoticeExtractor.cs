namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Wraps a provider with a deadline and the guarantee that a call returns rather than throws.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One attempt. No retries.</strong> A provider is a black box: if it does not answer,
/// that is the answer, and it is recorded as plainly as a success would be. A reviewer sees
/// which provider failed and why, and decides — run it again, use another provider, or type
/// the values in by hand. That decision belongs to a person looking at the notice, not to a
/// backoff schedule guessing on their behalf.
/// </para>
/// <para>
/// Retrying would buy very little and cost a lot. The notice is not lost while nobody retries
/// it; it sits in the queue with a failed extraction against it, which is a visible state
/// somebody can act on. Against that, retries add a schedule to reason about, a worst-case
/// duration several times the timeout, and a class of failure that gets quietly absorbed
/// rather than reported.
/// </para>
/// <para>
/// Two things it still does, both one line each:
/// </para>
/// <para>
/// <strong>A deadline</strong>, so a hung provider cannot wedge the ingestion loop. The notice
/// is still on disk or in the mailbox; giving up costs nothing.
/// </para>
/// <para>
/// <strong>A result rather than an exception</strong>, so every attempt leaves a row — and the
/// failures most of all, since those are what a reviewer needs to see. Whatever a provider
/// throws, including a bug in the provider itself, comes back as a recorded failure. Only
/// genuine cancellation propagates.
/// </para>
/// </remarks>
internal sealed class GuardedNoticeExtractor(
    INoticeExtractor inner,
    ProviderOptions options,
    TimeProvider? timeProvider = null) : INoticeExtractor
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string ModelName => inner.ModelName;

    public async Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();

        // Linked, so the caller's shutdown still wins, plus a deadline of our own. Which token
        // fired is what tells the two apart afterwards: a deadline is a failure to record, a
        // shutdown is not a failure at all.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(options.Timeout);

        try
        {
            var result = await inner.ExtractAsync(pdfBytes, noticeType, attempt.Token);

            return result with
            {
                Telemetry = result.Telemetry with { Latency = _time.GetElapsedTime(started) },
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Not a failed extraction — the notice is untouched and will be
            // picked up again, so there is nothing to record and nobody to tell.
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExtractionResult.Timeout(
                ModelName,
                string.Empty,
                new ExtractionTelemetry(Latency: _time.GetElapsedTime(started)),
                $"No response within {options.Timeout.TotalSeconds:N0}s.");
        }
        catch (Exception ex)
        {
            // Everything, deliberately — not just ExtractionProviderException. A provider that
            // throws something unexpected is a bug in that provider, and a bug there must not
            // become a notice with no row against it. The decorator owns the row; the provider
            // owns the fault.
            return ExtractionResult.ProviderFailure(
                ModelName,
                string.Empty,
                new ExtractionTelemetry(Latency: _time.GetElapsedTime(started)),
                Describe(ex));
        }
    }

    /// <summary>One line, and never the key, the token, or any of the notice.</summary>
    private static string Describe(Exception ex) =>
        ex is ExtractionProviderException { StatusCode: int status } provider
            ? $"{provider.Message} (HTTP {status})"
            : $"{ex.GetType().Name}: {ex.Message}";
}
