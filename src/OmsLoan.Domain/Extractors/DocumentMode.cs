namespace OmsLoan.Domain.Extractors;

/// <summary>
/// How the notice reached the model: as the document, or as text pulled out first.
/// </summary>
/// <remarks>
/// Stamped on the extraction, not only configured on the provider. Configuration says what a
/// provider does now; an accuracy report asks what a run did months ago, after settings may have changed.
///
/// Without this stamp, comparing Claude against Groq compares a native read against a flattened
/// one and blames the model. The text path keeps reading order but not columns — a rate table
/// arrives as rows whose headings must be re-associated. That handicap belongs to preprocessing.
/// </remarks>
public enum DocumentMode
{
    /// <summary>The PDF itself went to the model.</summary>
    Native,

    /// <summary>Text was extracted first; the model never saw the layout.</summary>
    ExtractedText,
}
