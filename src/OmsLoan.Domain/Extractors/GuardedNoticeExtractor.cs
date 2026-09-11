namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Wraps a provider with a deadline and the guarantee that a call returns a result rather than throws.
/// </summary>
/// <remarks>
/// One attempt. No retries. A provider that does not answer is the answer, recorded as plainly
/// as a success. A reviewer decides whether to rerun, switch providers, or type values in —
/// not a backoff schedule guessing on their behalf.
///
/// A deadline so a hung provider cannot wedge ingestion. A result rather than an exception so
/// every attempt leaves a row, failures most of all. Only genuine cancellation propagates.
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

        // Linked so caller shutdown still wins, plus our own deadline. Which token fired
        // separates them: deadline is a recorded failure; shutdown is not a failure at all.
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
            // Shutting down. Notice untouched; it will be picked up again. Nothing to record.
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
            // Everything, deliberately — not just ExtractionProviderException. A provider bug
            // must not become a notice with no row. The decorator owns the row; the provider owns the fault.
            return ExtractionResult.ProviderFailure(
                ModelName,
                string.Empty,
                new ExtractionTelemetry(Latency: _time.GetElapsedTime(started)),
                Describe(ex));
        }
    }

    /// <summary>One line. Never the key, the token, or any of the notice.</summary>
    private static string Describe(Exception ex) =>
        ex is ExtractionProviderException { StatusCode: int status } provider
            ? $"{provider.Message} (HTTP {status})"
            : $"{ex.GetType().Name}: {ex.Message}";
}
