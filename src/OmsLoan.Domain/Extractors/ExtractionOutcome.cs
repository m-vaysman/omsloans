namespace OmsLoan.Domain.Extractors;

/// <summary>
/// How an extraction attempt ended.
/// </summary>
/// <remarks>
/// Every value produces a stored row. A failed attempt is the most useful evidence — it is how
/// you tell later whether a notice was misread, never read, or read from a response nobody looked at.
/// </remarks>
public enum ExtractionOutcome
{
    /// <summary>The provider answered and the response parsed.</summary>
    Succeeded,

    /// <summary>
    /// The provider answered and the response did not parse — truncated, not JSON, or schema
    /// mismatch. The raw response is still recorded; that is the point.
    /// </summary>
    ParseFailed,

    /// <summary>
    /// The provider refused or errored: auth, quota, bad request, or 5xx. There is no usable
    /// response to parse. There is no retry inside the call — see <see cref="GuardedNoticeExtractor"/>.
    /// </summary>
    ProviderFailed,

    /// <summary>
    /// The call outlasted its timeout. Separate from a provider error because the remedy differs:
    /// a longer timeout or a smaller document, not a credential.
    /// </summary>
    TimedOut,
}
