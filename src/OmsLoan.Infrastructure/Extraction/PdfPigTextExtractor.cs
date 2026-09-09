using System.Text;
using System.Text.RegularExpressions;
using OmsLoan.Domain.Extractors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace OmsLoan.Infrastructure.Extraction;

/// <summary>
/// PDF to text, for providers that cannot be sent the document.
/// </summary>
/// <remarks>
/// <para>
/// Only Groq needs this. Claude and OpenAI take the PDF itself, and routing them through here
/// would throw away the layout that ADR 0001 chose them for.
/// </para>
/// <para>
/// PdfPig rather than iText: Apache-2.0 against iText's AGPL, which would need a commercial
/// licence for a product like this one. It is also pure managed, so nothing native has to be
/// present on a Windows Service host.
/// </para>
/// <para>
/// Words are recovered by position and then segmented into lines, rather than read in the
/// order the PDF happens to store them. That is not cosmetic. Content streams are frequently
/// written column-first or in whatever order the producing application emitted, and a naive
/// read gives a rate table as an interleaved run with no line breaks at all — every label
/// separated from its value. Segmenting puts them back together:
/// <c>Principal Amount €5,408,157.64</c> on one line is a thing a model can read.
/// </para>
/// <para>
/// It is still lossy, and the loss is worth naming. Line structure comes back; column
/// structure does not, so a table with three rate columns arrives as rows of numbers whose
/// headings the model has to re-associate. Anything overlaid on the page — a watermark, a
/// received stamp — is text too, and lands interleaved between content lines. Both are
/// reasons the extraction records that it came through this path.
/// </para>
/// </remarks>
public sealed partial class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <summary>Horizontal runs only: line breaks carry the row structure and are kept.</summary>
    private static readonly Regex Whitespace = HorizontalWhitespace();

    [GeneratedRegex("[ \\t]+")]
    private static partial Regex HorizontalWhitespace();

    public string Extract(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var text = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                if (text.Length > 0)
                {
                    text.Append("\n\n--- page ").Append(page.Number).Append(" ---\n\n");
                }

                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);

                foreach (var block in DefaultPageSegmenter.Instance.GetBlocks(words))
                {
                    text.AppendLine(block.Text);
                }
            }

            // Runs of spaces collapse to one. The word recovery leaves several between words,
            // and every one of them is a token this provider is billed for — which matters
            // because the text path exists for the cheap provider.
            var extracted = Whitespace.Replace(text.ToString(), " ").Trim();

            // A PDF that opens and yields nothing is a scan: pages of images with no text
            // layer. Returning the empty string would send the model a notice that says
            // nothing, and it would answer with a confident extraction of no fields — a
            // success row for a document nobody read.
            if (extracted.Length == 0)
            {
                throw new ExtractionProviderException(
                    "The PDF has no text layer, so it is almost certainly a scan. This provider "
                    + "cannot read it; route the notice to one that takes documents natively.");
            }

            return extracted;
        }
        catch (ExtractionProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExtractionProviderException($"The PDF could not be read: {ex.Message}", inner: ex);
        }
    }
}
