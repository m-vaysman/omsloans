using System.Text.RegularExpressions;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// What the gate concluded about a notice before any provider was asked.
/// </summary>
/// <param name="Type">
/// Highest-priority type recognised, or <see cref="NoticeType.Unknown"/>. Priority is signal
/// table order — see <see cref="NoticeClassifier"/>. A hint the extractor may disagree with.
/// </param>
/// <param name="Types">Every type the text suggests — how a combined notice is spotted.</param>
/// <param name="HasRateTable">
/// Whether rates appear. The expensive-read signal: rates mean a table, and a table survives
/// a native document read and does not survive a text one.
/// </param>
public sealed record Classification(
    NoticeType Type,
    IReadOnlyList<NoticeType> Types,
    bool HasRateTable)
{
    /// <summary>Nothing recognised, or no text to read.</summary>
    public static readonly Classification None =
        new(NoticeType.Unknown, [], HasRateTable: false);

    /// <summary>More than one economic event suggested — a per-type prompt would lose one.</summary>
    public bool IsCombined => Types.Count > 1;
}

/// <summary>
/// Reads extracted text and guesses what the notice is, without asking a model.
/// </summary>
/// <remarks>
/// Decided on #70. A cheap model over the PDF cost ~2,250 tokens — roughly a Sonnet extraction
/// in tokens. On extracted text the gate answers something coarser than extraction: is there a
/// rate table, what does this look like. That is phrases. Zero tokens, zero latency, deterministic.
///
/// Allowed to be wrong. #68 makes the type a hint; <see cref="Classification.None"/> costs a
/// more expensive read rather than a wrong answer. That asymmetry is why phrase matching belongs
/// here and would not belong in extraction itself.
/// </remarks>
public static partial class NoticeClassifier
{
    /// <summary>
    /// Matches a phrase only where it stands as its own word or words.
    /// </summary>
    /// <remarks>
    /// Not <c>Contains</c>: <c>estr</c> sits inside <c>restricted</c>, which appears on many
    /// credit documents — a substring test reported a rate table on notices that only mentioned
    /// restricted payments, and routed a cheap notice to an expensive document read.
    ///
    /// Not <c>\b</c> either. The euro form is written <c>€STR</c>; <c>€</c> is not a word
    /// character, so there is no boundary between a space and it. Lookarounds ask "not adjacent
    /// to a letter or digit" — admits a leading symbol, still refuses a match buried in a longer word.
    /// </remarks>
    private static Regex Matcher(string phrase) =>
        new(@"(?<![\p{L}\p{N}])" + Regex.Escape(phrase) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex[] Matchers(params string[] phrases) => [.. phrases.Select(Matcher)];

    /// <summary>
    /// Phrases that indicate a type, in wording agent banks actually use.
    /// </summary>
    /// <remarks>
    /// Phrases, not single words: "rate" appears on almost every notice; "Base Rate" does not.
    ///
    /// Order is priority. <see cref="Classification.Type"/> takes the first match, so a notice
    /// stating both a reset and a payment reports the reset — the reset needs the document read.
    /// Reordering this table changes which type a combined notice reports.
    /// </remarks>
    private static readonly (NoticeType Type, Regex[] Phrases)[] Signals =
    [
        (NoticeType.RateReset,
            Matchers("rate reset", "interest rate reset", "rate set date", "repricing", "reset effective")),

        (NoticeType.InterestPayment,
            Matchers("interest accrual", "accrued interest", "interest payment", "interest due", "interest period")),

        (NoticeType.PrincipalPayment,
            Matchers("principal payment", "principal amount", "paydown", "outstanding principal", "prepayment")),

        (NoticeType.Drawdown,
            Matchers("drawdown", "draw down", "borrowing request", "advance request", "funding date")),

        (NoticeType.CommitmentReduction,
            Matchers("commitment reduction", "commitment reduced", "reduction of commitment", "unfunded commitment")),

        (NoticeType.Rollover,
            Matchers("rollover", "roll over", "maturing contract", "continuation of")),

        (NoticeType.Fee,
            Matchers("fee amount", "commitment fee", "amendment fee", "agency fee", "upfront fee", "fee type")),
    ];

    /// <summary>
    /// Rates mean a table, and a table is what a text read loses.
    /// </summary>
    /// <remarks>
    /// The cost signal. Notices with rates should go to a provider reading the document: tranche
    /// and rate association does not survive text extraction. A false negative here is the
    /// expensive mistake — a rate table through a text extractor, then the model blamed for the loss.
    /// </remarks>
    private static readonly Regex[] RateTablePhrases = Matchers(
        "base rate", "applicable margin", "all-in rate", "all in rate", "day count",
        "term sofr", "daily simple sofr", "euribor", "sonia",

        // Both spellings. Real notices use either; the symbol form is what a naive substring miss.
        "estr", "€str");

    /// <summary>
    /// Classifies extracted text. Empty or unreadable text yields <see cref="Classification.None"/>,
    /// not a throw.
    /// </summary>
    /// <remarks>
    /// Null and whitespace are ordinary. A scanned notice has no text layer; the gate failing
    /// must never lose a notice — it degrades to no hint and the extractor reads the document.
    /// </remarks>
    public static Classification Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Classification.None;

        var found = new List<NoticeType>();

        foreach (var (type, phrases) in Signals)
        {
            if (phrases.Any(phrase => phrase.IsMatch(text)))
            {
                found.Add(type);
            }
        }

        var hasRates = RateTablePhrases.Any(phrase => phrase.IsMatch(text));

        if (found.Count == 0)
        {
            // Rates alone still matter: routes to a document read even without a type.
            return new Classification(NoticeType.Unknown, [], hasRates);
        }

        return new Classification(found[0], found, hasRates);
    }
}
