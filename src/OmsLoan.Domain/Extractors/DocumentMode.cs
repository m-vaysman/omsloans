namespace OmsLoan.Domain.Extractors;

/// <summary>
/// How the notice reached the model: as the document, or as text pulled out of it first.
/// </summary>
/// <remarks>
/// <para>
/// Recorded on the extraction rather than only configured on the provider, and the difference
/// matters. Configuration says what a provider does <em>now</em>; an accuracy report asks what
/// a particular run did months ago, and by then the setting may have changed, the provider may
/// have been reconfigured, or the model may have gained document support it did not have.
/// </para>
/// <para>
/// Without this stamp, comparing a Claude extraction against a Groq one compares a native read
/// against a flattened one and attributes the difference to the model. The text path recovers
/// reading order and line structure but not columns, so a rate table arrives as rows whose
/// headings have to be re-associated — a real handicap that belongs to the preprocessing step,
/// not to whatever read the result.
/// </para>
/// </remarks>
public enum DocumentMode
{
    /// <summary>The PDF itself went to the model.</summary>
    Native,

    /// <summary>Text was extracted from the PDF first, and the model never saw the layout.</summary>
    ExtractedText,
}
