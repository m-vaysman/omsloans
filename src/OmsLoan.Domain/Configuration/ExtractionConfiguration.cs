using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OmsLoan.Domain.Configuration;

public class ExtractionConfiguration : IEntityTypeConfiguration<Extraction>
{
    public void Configure(EntityTypeBuilder<Extraction> builder)
    {
        builder.ToTable("Extractions");

        builder.HasKey(e => e.ExtractionId);

        builder.Property(e => e.RawJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(e => e.ModelName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.PromptVersion)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(e => e.Notice)
            .WithMany(n => n.Extractions)
            .HasForeignKey(e => e.NoticeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Current-value reads filter on IsCurrent; keep it in the index or every read scans
        // a notice's full reprocessing history.
        builder.HasIndex(e => new { e.NoticeId, e.IsCurrent });
    }
}
