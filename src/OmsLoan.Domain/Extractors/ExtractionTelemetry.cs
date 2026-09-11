namespace OmsLoan.Domain.Extractors;

/// <summary>
/// What one provider call cost and how it ended, recorded whether it worked or not.
/// </summary>
/// <remarks>
/// Captured on failures as much as successes. A provider truncating shows rising completion
/// tokens against a <c>length</c> finish reason before values look wrong; a slowing provider
/// shows in latency before it shows as a timeout.
/// </remarks>
/// <param name="PromptTokens">Tokens billed for the request, when the provider reports them.</param>
/// <param name="CompletionTokens">Tokens billed for the response, when the provider reports them.</param>
/// <param name="Latency">Wall clock for the call, measured by the guard rather than the provider.</param>
/// <param name="FinishReason">
/// Provider's own word for why it stopped — <c>stop</c>, <c>length</c>, <c>content_filter</c>.
/// Kept verbatim: vendors disagree on vocabulary, and an unanticipated value is worth more in
/// the log than one flattened to "other".
/// </param>
public sealed record ExtractionTelemetry(
    int? PromptTokens = null,
    int? CompletionTokens = null,
    TimeSpan Latency = default,
    string? FinishReason = null,
    DocumentMode DocumentMode = DocumentMode.Native,
    TimeSpan QueueWait = default)
{
    /// <summary>Total billed tokens, when both halves are known.</summary>
    public int? TotalTokens =>
        PromptTokens is null && CompletionTokens is null
            ? null
            : (PromptTokens ?? 0) + (CompletionTokens ?? 0);
}
