using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OmsLoan.Domain.Configuration;

public class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    public void Configure(EntityTypeBuilder<Notice> builder)
    {
        builder.ToTable("Notices");

        builder.HasKey(n => n.NoticeId);

        builder.Property(n => n.Content)
            .HasColumnType("varbinary(max)")
            .IsRequired();

        // Lowercase hex digest: fixed width and ASCII, so char rather than nvarchar.
        builder.Property(n => n.Sha256)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(n => n.Sender)
            .HasMaxLength(320);

        builder.Property(n => n.EmailMessageId)
            .HasMaxLength(512)
            .IsUnicode(false);

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.ReceivedAtUtc)
            .IsRequired();

        builder.HasIndex(n => n.Sha256)
            .IsUnique();

        // Filtered, so the many notices with no message id do not collide with each other.
        builder.HasIndex(n => n.EmailMessageId)
            .IsUnique()
            .HasFilter("[EmailMessageId] IS NOT NULL");
    }
}
