using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// The lossy path, tested against real generated notices rather than a contrived PDF.
/// </summary>
/// <remarks>
/// These run on the frozen corpus, which means the assertions can be about content: the
/// borrower's name and the amounts are known, because the same spec produced both the PDF and
/// the expected extraction.
/// </remarks>
public class PdfPigTextExtractorTests
{
    private static readonly PdfPigTextExtractor Extractor = new();

    private static byte[] Corpus(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "corpus", name));

    [Fact]
    public void TheCorpusIsLinkedIntoThisProject()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "corpus");

        Assert.True(Directory.Exists(directory), $"No corpus at {directory}.");
        Assert.Equal(8, Directory.GetFiles(directory, "*.pdf").Length);
    }

    /// <summary>
    /// The economics survive the trip, which is what makes the Groq path usable at all for
    /// simple notices.
    /// </summary>
    [Fact]
    public void TheEconomicsAppearInTheExtractedText()
    {
        var text = Extractor.Extract(Corpus("004-principal-payment-t4.pdf"));

        Assert.Contains("PRINCIPAL PAYMENT", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Principal Amount", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Facility ID", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Both events survive on a combined notice. If one vanished here, a Groq extraction of
    /// that notice would be missing an event for a reason that has nothing to do with the
    /// model or the prompt.
    /// </summary>
    [Fact]
    public void BothEventsOfACombinedNoticeSurvive()
    {
        var text = Extractor.Extract(Corpus("005-combined-paydown-and-rate-reset-t5.pdf"));

        Assert.Contains("PRINCIPAL PAYMENT", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTEREST RATE RESET", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every layout yields text. Five templates exist because agent banks do not agree on one,
    /// and a template this path cannot read is a whole class of notice silently failing on the
    /// cheap provider.
    /// </summary>
    [Fact]
    public void EveryTemplateInTheBaselineYieldsText()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "corpus");

        foreach (var pdf in Directory.GetFiles(directory, "*.pdf"))
        {
            var text = Extractor.Extract(File.ReadAllBytes(pdf));

            Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(pdf)} produced no text.");
            Assert.Contains("Borrower", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Bytes that are not a PDF fail rather than returning nothing. An empty string would
    /// reach the model as a notice that says nothing, and come back as a confident extraction
    /// of no fields — a success row for a document nobody read.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAPdfThrowsRatherThanReturningEmpty()
    {
        var ex = Assert.Throws<ExtractionProviderException>(
            () => Extractor.Extract("this is not a pdf"u8.ToArray()));

        Assert.Contains("could not be read", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANullBodyIsRejectedAtTheBoundary() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.Extract(null!));
}
