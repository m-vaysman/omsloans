using Microsoft.EntityFrameworkCore;
using OmsLoan.Domain;

namespace OmsLoan.Domain.Tests;

/// <summary>
/// Mapping of <see cref="ExtractedField"/> — the EAV columns, the typed projections, and
/// the correction columns that sit beside the raw value rather than replacing it.
/// </summary>
public class ExtractedFieldModelTests
{
    [Fact]
    public void ExtractedField_MapsToExtractedFieldsTable()
    {
        Assert.Equal("ExtractedFields", DomainModel.Entity<ExtractedField>().GetTableName());
    }

    [Fact]
    public void NumericValue_IsScaledForRatesNotJustAmounts()
    {
        var numeric = DomainModel.Property<ExtractedField>(nameof(ExtractedField.NumericValue));

        // decimal(18,6): a spread quoted to four or five decimal places has to survive the
        // round trip, and reporting compares parsed values rather than strings.
        Assert.Equal(18, numeric.GetPrecision());
        Assert.Equal(6, numeric.GetScale());
        Assert.True(numeric.IsNullable);
    }

    [Fact]
    public void Confidence_IsScaledForAZeroToOneScore()
    {
        var confidence = DomainModel.Property<ExtractedField>(nameof(ExtractedField.Confidence));

        Assert.Equal(5, confidence.GetPrecision());
        Assert.Equal(4, confidence.GetScale());

        // Nullable: not every provider returns a confidence, and absent is not zero.
        Assert.True(confidence.IsNullable);
    }

    [Fact]
    public void RawValue_IsNullable_SoAMissedFieldIsDistinctFromAWrongOne()
    {
        var rawValue = DomainModel.Property<ExtractedField>(nameof(ExtractedField.RawValue));

        Assert.True(rawValue.IsNullable);
        Assert.Equal(4000, rawValue.GetMaxLength());
    }

    [Fact]
    public void FieldName_IsRequired()
    {
        var fieldName = DomainModel.Property<ExtractedField>(nameof(ExtractedField.FieldName));

        // The vocabulary is pinned by the per-notice-type prompt schema, not the database,
        // but a field with no name is meaningless in any of them.
        Assert.False(fieldName.IsNullable);
        Assert.Equal(128, fieldName.GetMaxLength());
    }

    [Fact]
    public void CorrectionColumns_AreMapped_AndOptional()
    {
        // Mapped despite having private setters: EF must persist them, and the accuracy
        // report reads them. Optional because most fields are never corrected.
        var value = DomainModel.Property<ExtractedField>(nameof(ExtractedField.CorrectedValue));
        var by = DomainModel.Property<ExtractedField>(nameof(ExtractedField.CorrectedBy));
        var at = DomainModel.Property<ExtractedField>(nameof(ExtractedField.CorrectedAtUtc));

        Assert.True(value.IsNullable);
        Assert.True(by.IsNullable);
        Assert.True(at.IsNullable);
        Assert.Equal(4000, value.GetMaxLength());
        Assert.Equal(256, by.GetMaxLength());
    }

    [Fact]
    public void CorrectedValue_AndRawValue_AreSeparateColumns()
    {
        var raw = DomainModel.Property<ExtractedField>(nameof(ExtractedField.RawValue));
        var corrected = DomainModel.Property<ExtractedField>(nameof(ExtractedField.CorrectedValue));

        // Two columns, not one. The pair is the labelled example the accuracy report is
        // built on; mapping them onto the same column would silently destroy it.
        Assert.NotEqual(raw.GetColumnName(), corrected.GetColumnName());
        Assert.Equal(4000, raw.GetMaxLength());
        Assert.Equal(4000, corrected.GetMaxLength());
    }

    [Fact]
    public void DeletingAnExtraction_IsRestricted_SoCorrectionsSurviveReprocessing()
    {
        var foreignKey = Assert.Single(
            DomainModel.Entity<ExtractedField>().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Extraction));

        // Reprocessing supersedes an extraction rather than deleting it; restrict makes
        // deleting one that still carries corrections an error instead of a silent loss.
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void FieldLookupWithinAnExtraction_IsIndexed()
    {
        var index = DomainModel.Index<ExtractedField>(
            nameof(ExtractedField.ExtractionId),
            nameof(ExtractedField.FieldName));

        // The accuracy report groups correction rates by field name within an extraction.
        Assert.False(index.IsUnique);
    }
}
