namespace OmsLoan.Domain.Extractors;

/// <summary>
/// What one call to a provider cost and how it ended, recorded whether it worked or not.
/// </summary>
/// <remarks>
/// Captured on failures as much as successes, which is the part that is easy to skip and
/// expensive to add back. A provider that has started truncating responses shows up as rising
/// completion tokens against a <c>length</c> finish reason long before anybody notices the
/// extracted values are wrong, and a provider that has become slow shows up in latency before
/// it shows up as a timeout.
/// </remarks>
/// <param name="PromptTokens">Tokens billed for the request, when the provider reports them.</param>
/// <param name="CompletionTokens">Tokens billed for the response, when the provider reports them.</param>
/// <param name="Latency">Wall clock for the call, measured by the guard rather than the provider.</param>
/// <param name="FinishReason">
/// The provider's own word for why it stopped — <c>stop</c>, <c>length</c>, <c>content_filter</c>
/// and so on. Kept verbatim rather than mapped to an enum: the vendors do not agree on the
/// vocabulary, and a value nobody anticipated is worth more in the log than one flattened to
/// "other".
/// </param>
public sealed record ExtractionTelemetry(
    int? PromptTokens = null,
    int? CompletionTokens = null,
    TimeSpan Latency = default,
    string? FinishReason = null)
{
    /// <summary>Total billed tokens, when both halves are known.</summary>
    public int? TotalTokens =>
        PromptTokens is null && CompletionTokens is null
            ? null
            : (PromptTokens ?? 0) + (CompletionTokens ?? 0);
}
