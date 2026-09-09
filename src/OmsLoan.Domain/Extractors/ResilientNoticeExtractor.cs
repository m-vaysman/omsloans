namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Wraps a provider with a timeout, retries worth making, and the guarantee that a call
/// returns rather than throws.
/// </summary>
/// <remarks>
/// <para>
/// Three jobs, and each exists because of a way the Worker could otherwise be hurt.
/// </para>
/// <para>
/// <strong>A timeout, so a hung provider cannot wedge the loop.</strong> The notice is still
/// on disk or in the mailbox — nothing is at risk by giving up and coming back to it. A
/// Worker blocked on one document while a queue builds behind it is a much worse state than
/// one extraction that has to be retried.
/// </para>
/// <para>
/// <strong>Retries, but only ones that can work.</strong> A 429 or a 503 is weather. A 401 or
/// a 400 is a fact about the request: retrying it three times with backoff turns a
/// five-second failure into a thirty-second one, calls the provider three times to be told
/// the same thing, and delays the log entry that says what is actually wrong.
/// </para>
/// <para>
/// <strong>A result rather than an exception.</strong> Every attempt produces something worth
/// storing, and the failures are the ones worth storing most — an unparseable response is
/// exactly the case somebody needs to look at later. Only genuine cancellation propagates.
/// </para>
/// <para>
/// Backoff is exponential with full jitter. Without jitter, a batch of notices failing
/// together retries together, and a provider that rate-limited us once gets the same burst
/// again a second later.
/// </para>
/// </remarks>
public sealed class ResilientNoticeExtractor(
    INoticeExtractor inner,
    ProviderOptions options,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<double>? jitter = null) : INoticeExtractor
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((wait, token) => Task.Delay(wait, token));

    // Injectable so a test can pin the jitter and assert the schedule instead of a range.
    private readonly Func<double> _jitter = jitter ?? Random.Shared.NextDouble;

    public string ModelName => inner.ModelName;

    public async Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var attempts = 0;
        Exception? last = null;
        var timedOut = false;

        while (attempts < Math.Max(1, options.MaxAttempts))
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();

            // Linked, so the caller's shutdown still wins, plus a deadline of our own. The two
            // are told apart afterwards by asking which token actually fired: a timeout is a
            // failure to record, while a shutdown is not a failure at all.
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(options.Timeout);

            try
            {
                var result = await inner.ExtractAsync(pdfBytes, noticeType, attemptCancellation.Token);

                return result with
                {
                    Telemetry = result.Telemetry with
                    {
                        Latency = _time.GetElapsedTime(started),
                        Attempts = attempts,
                    },
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down. Not a failed extraction — the notice is untouched and will be
                // picked up again, so there is nothing to record and nobody to tell.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // Our deadline, not the caller's. Worth retrying: a provider slow once is
                // often not slow twice, and the alternative is one long document permanently
                // unreadable.
                timedOut = true;
                last = ex;
            }
            catch (ExtractionProviderException ex) when (ex.IsTransient)
            {
                timedOut = false;
                last = ex;
            }
            catch (ExtractionProviderException ex)
            {
                // Permanent. Stop immediately: nothing about calling again changes the answer.
                return ExtractionResult.ProviderFailure(
                    ModelName,
                    PromptVersionOf(last: ex),
                    new ExtractionTelemetry(Latency: _time.GetElapsedTime(started), Attempts: attempts),
                    Describe(ex));
            }

            if (attempts >= Math.Max(1, options.MaxAttempts))
            {
                break;
            }

            await _delay(NextBackoff(attempts), cancellationToken);
        }

        var telemetry = new ExtractionTelemetry(Latency: _time.GetElapsedTime(started), Attempts: attempts);

        return timedOut
            ? ExtractionResult.Timeout(
                ModelName,
                string.Empty,
                telemetry,
                $"No response within {options.Timeout.TotalSeconds:N0}s, after {attempts} attempt(s).")
            : ExtractionResult.ProviderFailure(
                ModelName,
                string.Empty,
                telemetry,
                last is null ? "The provider call failed." : Describe(last));
    }

    /// <summary>
    /// Exponential with full jitter: a uniform pick from zero to the current ceiling.
    /// </summary>
    /// <remarks>
    /// Full jitter rather than a fixed step with a wobble, because the failure that matters is
    /// correlated — a batch of notices hitting a rate limit together will otherwise retry
    /// together and re-create the burst that caused it.
    /// </remarks>
    private TimeSpan NextBackoff(int attempt)
    {
        var ceiling = options.RetryBaseDelay * Math.Pow(2, attempt - 1);

        return TimeSpan.FromMilliseconds(ceiling.TotalMilliseconds * _jitter());
    }

    /// <summary>One line, and never the key, the token, or any of the notice.</summary>
    private static string Describe(Exception ex) =>
        ex is ExtractionProviderException provider && provider.StatusCode is int status
            ? $"{provider.Message} (HTTP {status})"
            : ex.Message;

    /// <summary>
    /// The prompt version is the inner extractor's to report, and a call that never reached it
    /// has none. Empty rather than invented — a row claiming a prompt it never used is worse
    /// than one admitting it does not know.
    /// </summary>
    private static string PromptVersionOf(Exception last) => string.Empty;
}
