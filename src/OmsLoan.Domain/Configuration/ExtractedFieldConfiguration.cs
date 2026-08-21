using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OmsLoan.Domain.Configuration;

public class ExtractedFieldConfiguration : IEntityTypeConfiguration<ExtractedField>
{
    public void Configure(EntityTypeBuilder<ExtractedField> builder)
    {
        builder.ToTable("ExtractedFields");

        builder.HasKey(f => f.ExtractedFieldId);

        builder.Property(f => f.FieldName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(f => f.RawValue)
            .HasMaxLength(4000);

        // Scaled for rates and spreads, not just payment amounts.
        builder.Property(f => f.NumericValue)
            .HasPrecision(18, 6);

        builder.Property(f => f.Confidence)
            .HasPrecision(5, 4);

        builder.Property(f => f.CorrectedValue)
            .HasMaxLength(4000);

        builder.Property(f => f.CorrectedBy)
            .HasMaxLength(256);

        builder.HasOne(f => f.Extraction)
            .WithMany(e => e.Fields)
            .HasForeignKey(f => f.ExtractionId)
            .OnDelete(DeleteBehavior.Restrict);

        // The accuracy report slices correction rates by field name within an extraction.
        builder.HasIndex(f => new { f.ExtractionId, f.FieldName });
    }
}
