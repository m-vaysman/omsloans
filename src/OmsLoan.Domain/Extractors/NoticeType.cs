namespace OmsLoan.Domain.Extractors;

/// <summary>
/// What kind of notice a document is, which decides the prompt and the schema used to read it.
/// </summary>
/// <remarks>
/// Defined here because <see cref="INoticeExtractor"/> needs it. Deciding which one a given
/// PDF is belongs to classification (#12), which owns this type from the moment it lands;
/// until then <see cref="Unknown"/> is what everything passes.
/// </remarks>
public enum NoticeType
{
    /// <summary>Not yet classified. The only value in use until classification lands.</summary>
    Unknown,

    /// <summary>A rate reset — the tabular one, and the reason PDFs are sent to the model verbatim.</summary>
    RateReset,

    InterestPayment,

    PrincipalPayment,

    Fee,
}
