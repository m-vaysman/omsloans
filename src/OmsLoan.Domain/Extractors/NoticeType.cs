namespace OmsLoan.Domain.Extractors;

/// <summary>
/// A kind of economic event a notice can describe.
/// </summary>
/// <remarks>
/// <para>
/// Not a property of the notice. One document can describe several — a principal paydown and
/// the rate reset for the next period is an ordinary combination — so a notice carries a list
/// of events, each with its own type, rather than being "a rate reset notice".
/// </para>
/// <para>
/// <see cref="WireName"/> is what appears in the model's JSON and in stored field names. It is
/// snake_case because that is what the schema constrains the model to emit, and because a
/// value that round-trips unchanged is one fewer place for a mapping to be wrong.
/// </para>
/// </remarks>
public enum NoticeType
{
    /// <summary>
    /// An event the model could not type. Emitted rather than dropped: a document containing
    /// something we cannot name is a thing a reviewer needs to see, and guessing at it
    /// produces confidently wrong economics.
    /// </summary>
    Unknown,

    /// <summary>The tabular one, and why PDFs go to the model verbatim.</summary>
    RateReset,

    InterestPayment,

    PrincipalPayment,

    Fee,

    Rollover,

    /// <summary>Money drawn under a facility.</summary>
    /// <remarks>
    /// Not a principal payment with the sign reversed. A paydown reduces the outstanding
    /// balance and a drawdown increases it; typing one as the other puts a borrower on the
    /// review screen repaying money they were in fact borrowing.
    /// </remarks>
    Drawdown,

    /// <summary>
    /// A commitment reduced, which happens with or without anything being drawn or repaid and
    /// is therefore its own event rather than a detail of one.
    /// </summary>
    CommitmentReduction,
}

/// <summary>Conversions between <see cref="NoticeType"/> and the strings in the schema.</summary>
public static class NoticeTypes
{
    private static readonly Dictionary<NoticeType, string> Names = new()
    {
        [NoticeType.Unknown] = "unknown",
        [NoticeType.RateReset] = "rate_reset",
        [NoticeType.InterestPayment] = "interest_payment",
        [NoticeType.PrincipalPayment] = "principal_payment",
        [NoticeType.Fee] = "fee",
        [NoticeType.Rollover] = "rollover",
        [NoticeType.Drawdown] = "drawdown",
        [NoticeType.CommitmentReduction] = "commitment_reduction",
    };

    private static readonly Dictionary<string, NoticeType> ByName =
        Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every type the schema allows, in the order the prompt lists them.</summary>
    public static IReadOnlyList<string> AllowedNames => [.. Names.Values];

    /// <summary>The string form used in the schema and in stored field names.</summary>
    public static string WireName(this NoticeType type) => Names[type];

    /// <summary>
    /// Parses a type the model emitted. Anything unrecognised becomes
    /// <see cref="NoticeType.Unknown"/> rather than throwing — a model inventing a type is a
    /// thing to record and show a reviewer, not a reason to lose the whole extraction.
    /// </summary>
    public static NoticeType Parse(string? wireName) =>
        wireName is not null && ByName.TryGetValue(wireName, out var type) ? type : NoticeType.Unknown;
}
