using System.Text.Json;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// The gate, scored against real notices rather than argued about.
/// </summary>
/// <remarks>
/// This is the test that decides whether #68 needs its two classify models at all. Every notice
/// in the frozen corpus has a known type by construction — the same spec produced the PDF and
/// the expected extraction — so the heuristic can be measured against ground truth over real
/// PDFs, through the real text extractor, without a single API call.
/// </remarks>
public class NoticeClassifierTests
{
    private static readonly PdfPigTextExtractor Text = new();

    private static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "corpus");

    private static string TextOf(string pdf) => Text.Extract(File.ReadAllBytes(pdf));

    /// <summary>The types the notice actually states, from its expected extraction.</summary>
    private static HashSet<NoticeType> TruthFor(string pdfPath)
    {
        var expected = Path.ChangeExtension(pdfPath, null) + ".expected.json";

        using var document = JsonDocument.Parse(File.ReadAllText(expected));

        return document.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => NoticeTypes.Parse(e.GetProperty("type").GetString()))
            .ToHashSet();
    }

    public static TheoryData<string> CorpusPdfs()
    {
        var data = new TheoryData<string>();

        foreach (var pdf in Directory.GetFiles(CorpusDirectory, "*.pdf"))
        {
            data.Add(Path.GetFileName(pdf));
        }

        return data;
    }

    /// <summary>
    /// The gate recognises at least one of the types the notice really states, on every notice
    /// in the baseline. Not every type — that is extraction's job — but enough to route.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusPdfs))]
    public void TheGateRecognisesAtLeastOneRealTypeOnEveryNotice(string fileName)
    {
        var path = Path.Combine(CorpusDirectory, fileName);
        var truth = TruthFor(path);

        var classification = NoticeClassifier.Classify(TextOf(path));

        Assert.NotEqual(NoticeType.Unknown, classification.Type);
        Assert.True(
            classification.Types.Any(truth.Contains),
            $"{fileName}: gate said [{string.Join(", ", classification.Types)}], notice states "
            + $"[{string.Join(", ", truth)}].");
    }

    /// <summary>
    /// The combined notice is spotted as combined. This is the case a per-type prompt could not
    /// express, so a gate that collapsed it to one type would route away the whole reason the
    /// events array exists.
    /// </summary>
    [Fact]
    public void TheCombinedNoticeIsSeenAsMoreThanOneEvent()
    {
        var path = Path.Combine(CorpusDirectory, "005-combined-paydown-and-rate-reset-t5.pdf");

        var classification = NoticeClassifier.Classify(TextOf(path));

        Assert.True(classification.IsCombined, $"Saw only [{string.Join(", ", classification.Types)}].");
        Assert.Contains(NoticeType.PrincipalPayment, classification.Types);
        Assert.Contains(NoticeType.RateReset, classification.Types);
    }

    /// <summary>
    /// The signal that actually decides cost: rates present means the notice wants a document
    /// read, because the row and column association in a rate table does not survive text
    /// extraction.
    /// </summary>
    [Theory]
    [InlineData("002-rate-reset-t2.pdf", true)]
    [InlineData("003-rate-reset-no-all-in-t3.pdf", true)]
    [InlineData("005-combined-paydown-and-rate-reset-t5.pdf", true)]
    [InlineData("004-principal-payment-t4.pdf", false)]
    [InlineData("008-revolver-draw-and-commitment-change-t3.pdf", false)]
    public void TheRateTableSignalMatchesWhetherTheNoticeStatesRates(string fileName, bool expected)
    {
        var classification = NoticeClassifier.Classify(TextOf(Path.Combine(CorpusDirectory, fileName)));

        Assert.Equal(expected, classification.HasRateTable);
    }

    /// <summary>
    /// Every layout, because a gate that worked on one template and not another would quietly
    /// route a whole class of notice to the wrong provider.
    /// </summary>
    [Fact]
    public void TheGateWorksAcrossAllFiveLayouts()
    {
        var scored = 0;

        foreach (var pdf in Directory.GetFiles(CorpusDirectory, "*.pdf"))
        {
            var classification = NoticeClassifier.Classify(TextOf(pdf));

            Assert.True(
                classification.Types.Any(TruthFor(pdf).Contains),
                $"{Path.GetFileName(pdf)} was not recognised.");

            scored++;
        }

        Assert.Equal(8, scored);
    }

    // --- degradation, which is the part that must never lose a notice -------------------------

    /// <summary>
    /// A scan has no text layer. The gate must return no hint and let the notice proceed to a
    /// provider that reads documents — the cheap gate becoming a new way to lose a notice would
    /// be a far worse outcome than the tokens it saves.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void NoTextMeansNoHintRatherThanAnError(string? text)
    {
        var classification = NoticeClassifier.Classify(text);

        Assert.Equal(NoticeType.Unknown, classification.Type);
        Assert.Empty(classification.Types);
        Assert.False(classification.HasRateTable);
        Assert.False(classification.IsCombined);
    }

    /// <summary>
    /// Text that recognises nothing still reports rates if they are there, because that is the
    /// routing decision and it is useful even with no type.
    /// </summary>
    [Fact]
    public void RatesAreReportedEvenWhenNoTypeIsRecognised()
    {
        var classification = NoticeClassifier.Classify("Some unfamiliar notice quoting Term SOFR and a Day Count.");

        Assert.Equal(NoticeType.Unknown, classification.Type);
        Assert.True(classification.HasRateTable);
    }

    /// <summary>
    /// And prose that merely says "rate" is not a rate table. The phrases are phrases rather
    /// than words precisely because "rate" appears on nearly every notice ever written.
    /// </summary>
    [Fact]
    public void TheWordRateAloneIsNotARateTable()
    {
        var classification = NoticeClassifier.Classify("We write regarding the rate at which your facility amortises.");

        Assert.False(classification.HasRateTable);
    }
}
