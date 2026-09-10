namespace OmsLoan.Domain.Extractors;

/// <summary>
/// What the gate concluded about a notice before any provider was asked.
/// </summary>
/// <param name="Type">
/// The dominant type, or <see cref="NoticeType.Unknown"/> when nothing was recognised. A hint
/// the extractor may disagree with, never a routing decision it has to obey.
/// </param>
/// <param name="Types">Every type the text suggests, which is how a combined notice is spotted.</param>
/// <param name="HasRateTable">
/// Whether rates appear at all. The expensive-read signal: rates mean a table, and a table is
/// what survives a native document read and does not survive a text one.
/// </param>
public sealed record Classification(
    NoticeType Type,
    IReadOnlyList<NoticeType> Types,
    bool HasRateTable)
{
    /// <summary>Nothing was recognised, or there was no text to read.</summary>
    public static readonly Classification None =
        new(NoticeType.Unknown, [], HasRateTable: false);

    /// <summary>More than one economic event is suggested, so a per-type prompt would lose one.</summary>
    public bool IsCombined => Types.Count > 1;
}

/// <summary>
/// The gate: reads extracted text and guesses what the notice is, without asking a model.
/// </summary>
/// <remarks>
/// <para>
/// Decided on #70. Classification originally ran a cheap model over the PDF, and the arithmetic
/// killed it: a PDF costs roughly 2,250 tokens whichever model reads it, so a Haiku gate over
/// the document cost about what a Sonnet extraction over the same document cost in tokens. It
/// was cheap in money and a second full-price read in tokens.
/// </para>
/// <para>
/// Moving the gate to extracted text dropped that to ~230 tokens — and then raised the obvious
/// question. The gate answers something far coarser than extraction: is there a rate table,
/// what does this look like. On text, that is keywords. So this costs no tokens, no latency and
/// no rate-limit slot, and unlike a model it returns the same answer twice.
/// </para>
/// <para>
/// It is measured rather than assumed. Every notice in the frozen corpus has a known type by
/// construction, so <c>NoticeClassifierTests</c> scores this against real PDFs with no API call
/// — which is what decides whether a model gate is needed at all.
/// </para>
/// <para>
/// <strong>It is allowed to be wrong.</strong> #68 makes the type a hint the extractor may
/// contradict, and a gate returning <see cref="Classification.None"/> costs a more expensive
/// read rather than a wrong answer. That asymmetry is why heuristics are defensible here and
/// would not be in the extraction itself.
/// </para>
/// </remarks>
public static class NoticeClassifier
{
    /// <summary>
    /// Phrases that indicate a type, in the wording agent banks actually use.
    /// </summary>
    /// <remarks>
    /// Each entry needs at least one match. They are deliberately phrases rather than single
    /// words: "rate" appears on almost every notice ever written, and "Base Rate" does not.
    /// </remarks>
    private static readonly (NoticeType Type, string[] Phrases)[] Signals =
    [
        (NoticeType.RateReset,
            ["rate reset", "interest rate reset", "rate set date", "repricing", "reset effective"]),

        (NoticeType.InterestPayment,
            ["interest accrual", "accrued interest", "interest payment", "interest due", "interest period"]),

        (NoticeType.PrincipalPayment,
            ["principal payment", "principal amount", "paydown", "outstanding principal", "prepayment"]),

        (NoticeType.Fee,
            ["fee amount", "commitment fee", "amendment fee", "agency fee", "upfront fee", "fee type"]),

        (NoticeType.Rollover,
            ["rollover", "roll over", "maturing contract", "continuation of"]),

        (NoticeType.Drawdown,
            ["drawdown", "draw down", "borrowing request", "advance request", "funding date"]),

        (NoticeType.CommitmentReduction,
            ["commitment reduction", "commitment reduced", "reduction of commitment", "unfunded commitment"]),
    ];

    /// <summary>
    /// Rates mean a table, and a table is the thing a text read loses.
    /// </summary>
    /// <remarks>
    /// This is the signal that actually decides cost. A notice with rates on it should go to a
    /// provider reading the document, because the row and column association between a tranche
    /// and its rate does not survive text extraction. A notice without them can take the cheap
    /// path safely.
    /// </remarks>
    private static readonly string[] RateTablePhrases =
    [
        "base rate", "applicable margin", "all-in rate", "all in rate",
        "day count", "term sofr", "euribor", "sonia", "estr", "daily simple sofr",
    ];

    /// <summary>
    /// Classifies extracted text. Empty or unreadable text yields
    /// <see cref="Classification.None"/> rather than throwing.
    /// </summary>
    /// <remarks>
    /// Null and whitespace are ordinary inputs, not errors. A scanned notice has no text layer,
    /// and the gate failing must never be a way to lose a notice — it degrades to no hint and
    /// the extractor reads the document, which is what it would have done anyway.
    /// </remarks>
    public static Classification Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Classification.None;

        var found = new List<NoticeType>();

        foreach (var (type, phrases) in Signals)
        {
            if (phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(type);
            }
        }

        var hasRates = RateTablePhrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        if (found.Count == 0)
        {
            // Rates and nothing else recognised is still worth reporting: it routes the notice to
            // a document read, which is the decision that matters even without a type.
            return new Classification(NoticeType.Unknown, [], hasRates);
        }

        return new Classification(found[0], found, hasRates);
    }
}
