namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Pulls text out of a PDF, for providers that cannot be sent the document itself.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a call into a library, for the same reason <see cref="INoticeExtractor"/>
/// is one: the implementation carries a package, and packages stay out of Domain.
/// </para>
/// <para>
/// This path is lossy and that is the point of naming it. A rate table becomes a run of text
/// with the row and column association gone, so which tranche owns which rate is a thing the
/// model now has to infer rather than read. ADR 0001 chose Claude precisely to avoid this
/// step; anything that goes through here is doing the harder version of the job, and the
/// extraction records that it did.
/// </para>
/// </remarks>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Text from every page, in order.
    /// </summary>
    /// <exception cref="ExtractionProviderException">
    /// The bytes are not a readable PDF. Thrown rather than returned empty: an empty string
    /// would reach the model as a notice that says nothing, and come back as a confident
    /// extraction of no fields rather than as the failure it is.
    /// </exception>
    string Extract(byte[] pdfBytes);
}
