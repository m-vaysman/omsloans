namespace OmsLoan.Domain.Extractors;

/// <summary>
/// How an extraction attempt ended.
/// </summary>
/// <remarks>
/// Every one of these produces a stored row. An attempt that failed is the most interesting
/// kind of evidence there is — it is the only way to tell later whether a notice was misread,
/// never read, or read from a response nobody has looked at.
/// </remarks>
public enum ExtractionOutcome
{
    /// <summary>The provider answered and the response parsed.</summary>
    Succeeded,

    /// <summary>
    /// The provider answered and the response did not parse — truncated, not JSON, or not
    /// matching the schema. The raw response is still recorded, and is the whole point.
    /// </summary>
    ParseFailed,

    /// <summary>
    /// The provider refused or errored: auth, quota, a bad request, a 5xx that outlasted the
    /// retries. There is no usable response to parse.
    /// </summary>
    ProviderFailed,

    /// <summary>
    /// The call outlasted its timeout. Recorded separately from a provider error because the
    /// remedy is different — a longer timeout or a smaller document, not a credential.
    /// </summary>
    TimedOut,
}
