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
/// Only Groq needs this. Claude and OpenAI take the PDF itself; routing them here would throw
/// away the layout ADR 0001 chose them for.
/// </para>
/// <para>
/// PdfPig rather than iText: Apache-2.0 vs AGPL (commercial licence for a product). Pure
/// managed — nothing native on an installed Windows service host.
/// </para>
/// <para>
/// Words recovered by position, then segmented into lines — not storage order. Content
/// streams are often column-first; a naive read interleaved a rate table with no line breaks.
/// Segmenting yields <c>Principal Amount €5,408,157.64</c> on one line.
/// </para>
/// <para>
/// Still lossy: line structure returns, column structure does not. Watermarks and stamps
/// land as interleaved text. Extraction records that it came through this path for both
/// reasons.
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

            // Collapse runs of spaces. Word recovery leaves several between words, and each
            // is a billed token — matters because the text path exists for the cheap provider.
            var extracted = Whitespace.Replace(text.ToString(), " ").Trim();

            // Opens but yields nothing: a scan with no text layer. Returning empty would send
            // the model a blank notice and get a confident empty extraction — a success row
            // for a document nobody read.
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
