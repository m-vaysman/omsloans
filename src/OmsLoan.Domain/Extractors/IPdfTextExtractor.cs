namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Pulls text out of a PDF for providers that cannot be sent the document itself.
/// </summary>
/// <remarks>
/// A seam rather than a library call — same reason as <see cref="INoticeExtractor"/>: the
/// implementation carries a package, and packages stay out of Domain.
///
/// This path is lossy, and naming it is the point. A rate table becomes a run of text with row
/// and column association gone; which tranche owns which rate is now inference, not reading.
/// ADR 0001 chose Claude to avoid this step; anything through here is the harder job, and the
/// extraction records that it did.
/// </remarks>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Text from every page, in order.
    /// </summary>
    /// <exception cref="ExtractionProviderException">
    /// Bytes are not a readable PDF. Thrown rather than returned empty: empty text would reach
    /// the model as a notice that says nothing and come back as a confident extraction of no
    /// fields rather than as the failure it is.
    /// </exception>
    string Extract(byte[] pdfBytes);
}
