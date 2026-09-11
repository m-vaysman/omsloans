namespace OmsLoan.Domain;

/// <summary>
/// One name/value pair read out of a notice, in EAV shape so a new notice type needs a new
/// prompt and schema rather than a migration.
/// </summary>
public class ExtractedField
{
    public int ExtractedFieldId { get; set; }

    public int ExtractionId { get; set; }

    public Extraction Extraction { get; set; } = null!;

    /// <summary>
    /// Field vocabulary is pinned by the per-notice-type prompt schema, not the database.
    /// Consistency here is only as good as the prompts that emit it.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// What the model said, immutable once written. Nullable because "found nothing" is a
    /// distinct outcome from getting the value wrong.
    /// </summary>
    public string? RawValue { get; set; }

    /// <summary>
    /// Typed projection of <see cref="RawValue"/> so numeric reporting does not degrade into
    /// string comparison. Scaled for rates, not just payment amounts.
    /// </summary>
    public decimal? NumericValue { get; set; }

    /// <summary>Typed projection of <see cref="RawValue"/> for date-valued fields.</summary>
    public DateTime? DateValue { get; set; }

    /// <summary>
    /// Model's stated confidence when the provider returns one. Whether it predicts correctness
    /// is what the accuracy report exists to answer.
    /// </summary>
    public decimal? Confidence { get; set; }

    /// <summary>Reviewer's corrected value, written alongside <see cref="RawValue"/>.</summary>
    public string? CorrectedValue { get; private set; }

    /// <summary>Reviewer who made the correction.</summary>
    public string? CorrectedBy { get; private set; }

    /// <summary>When the correction was made.</summary>
    public DateTime? CorrectedAtUtc { get; private set; }

    /// <summary>
    /// Records a reviewer's correction. Correction columns have private setters; this is their
    /// only entry point, so the extraction path cannot write them by accident. The labelled pair
    /// of what the model said and what was true is the long-term asset — an extraction that
    /// overwrote it would destroy the label at the moment it was created.
    /// </summary>
    public void ApplyCorrection(string? correctedValue, string correctedBy, DateTime correctedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctedBy);

        CorrectedValue = correctedValue;
        CorrectedBy = correctedBy;
        CorrectedAtUtc = correctedAtUtc;
    }
}
