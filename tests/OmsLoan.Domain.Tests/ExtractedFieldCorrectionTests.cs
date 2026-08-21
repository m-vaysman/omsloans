using System.Reflection;
using OmsLoan.Domain;

namespace OmsLoan.Domain.Tests;

/// <summary>
/// Behaviour of <see cref="ExtractedField.ApplyCorrection"/>.
/// </summary>
/// <remarks>
/// These are the highest-value tests in the suite. The accuracy report exists only because
/// what the model said and what a reviewer decided are both retained; the moment a
/// correction overwrites the raw value, the label is destroyed at the instant it is created
/// and no amount of later work recovers it. See
/// docs/decisions/0003-append-only-extractions-and-eav-fields.md.
/// </remarks>
public class ExtractedFieldCorrectionTests
{
    private static ExtractedField ExtractedRate() => new()
    {
        FieldName = "InterestRate",
        RawValue = "5.25",
        NumericValue = 5.25m,
        Confidence = 0.91m,
    };

    [Fact]
    public void ApplyCorrection_RecordsValueReviewerAndTimestamp()
    {
        var field = ExtractedRate();
        var correctedAt = new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc);

        field.ApplyCorrection("5.75", "mvaysman", correctedAt);

        Assert.Equal("5.75", field.CorrectedValue);
        Assert.Equal("mvaysman", field.CorrectedBy);
        Assert.Equal(correctedAt, field.CorrectedAtUtc);
    }

    [Fact]
    public void ApplyCorrection_LeavesRawValueUntouched()
    {
        var field = ExtractedRate();

        field.ApplyCorrection("5.75", "mvaysman", DateTime.UtcNow);

        // The whole point. A correction sits beside the model's answer, never on top of it.
        Assert.Equal("5.25", field.RawValue);
    }

    [Fact]
    public void ApplyCorrection_LeavesTypedProjectionsAndConfidenceUntouched()
    {
        var field = ExtractedRate();

        field.ApplyCorrection("5.75", "mvaysman", DateTime.UtcNow);

        // Confidence must survive too: the accuracy report asks whether a model's stated
        // confidence predicts correction, which is unanswerable if correcting rewrites it.
        Assert.Equal(5.25m, field.NumericValue);
        Assert.Equal(0.91m, field.Confidence);
    }

    [Fact]
    public void ApplyCorrection_AcceptsNull_WhenReviewerClearsAValueTheNoticeNeverStated()
    {
        var field = ExtractedRate();

        field.ApplyCorrection(null, "mvaysman", DateTime.UtcNow);

        // A model inventing a plausible rate is the expensive failure. Clearing it is a
        // correction in its own right, and is distinct from never having been reviewed.
        Assert.Null(field.CorrectedValue);
        Assert.Equal("mvaysman", field.CorrectedBy);
        Assert.NotNull(field.CorrectedAtUtc);
        Assert.Equal("5.25", field.RawValue);
    }

    [Fact]
    public void ApplyCorrection_AppliedTwice_KeepsLatestCorrectionAndStillNotTheRawValue()
    {
        var field = ExtractedRate();
        var second = new DateTime(2026, 8, 21, 16, 0, 0, DateTimeKind.Utc);

        field.ApplyCorrection("5.75", "mvaysman", new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc));
        field.ApplyCorrection("5.80", "areviewer", second);

        Assert.Equal("5.80", field.CorrectedValue);
        Assert.Equal("areviewer", field.CorrectedBy);
        Assert.Equal(second, field.CorrectedAtUtc);
        Assert.Equal("5.25", field.RawValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyCorrection_RequiresAReviewer(string? correctedBy)
    {
        var field = ExtractedRate();

        // An unattributable correction is worthless as a label — it cannot be traced back
        // to a person, and it is exactly what an automated write would look like.
        Assert.ThrowsAny<ArgumentException>(
            () => field.ApplyCorrection("5.75", correctedBy!, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyCorrection_WhenReviewerIsMissing_WritesNothingAtAll(string? correctedBy)
    {
        var field = ExtractedRate();

        Assert.ThrowsAny<ArgumentException>(
            () => field.ApplyCorrection("5.75", correctedBy!, DateTime.UtcNow));

        // Guard clause runs before assignment, so a rejected call leaves no partial state.
        Assert.Null(field.CorrectedValue);
        Assert.Null(field.CorrectedBy);
        Assert.Null(field.CorrectedAtUtc);
    }

    [Theory]
    [InlineData(nameof(ExtractedField.CorrectedValue))]
    [InlineData(nameof(ExtractedField.CorrectedBy))]
    [InlineData(nameof(ExtractedField.CorrectedAtUtc))]
    public void CorrectionProperties_HaveNoPublicSetter(string propertyName)
    {
        var setter = typeof(ExtractedField).GetProperty(propertyName)!.SetMethod;

        // ApplyCorrection is the only entry point by design; a public setter would let the
        // extraction path write a correction by accident and no test elsewhere would notice.
        Assert.NotNull(setter);
        Assert.False(setter!.IsPublic, $"{propertyName} must not expose a public setter.");
    }

    [Fact]
    public void RawValue_IsNullable_SoAModelFindingNothingIsDistinctFromGettingItWrong()
    {
        var field = new ExtractedField { FieldName = "InterestRate", RawValue = null };

        field.ApplyCorrection("5.25", "mvaysman", DateTime.UtcNow);

        // Null raw + a correction means the model missed the field; a wrong raw + a
        // correction means it misread it. The accuracy report reports these separately.
        Assert.Null(field.RawValue);
        Assert.Equal("5.25", field.CorrectedValue);
    }

    [Fact]
    public void NewField_HasNoCorrection()
    {
        var field = ExtractedRate();

        Assert.Null(field.CorrectedValue);
        Assert.Null(field.CorrectedBy);
        Assert.Null(field.CorrectedAtUtc);
    }
}
