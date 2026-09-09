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

        // Not unique. Ingestion records every arrival and only then moves the file, so the
        // same document can legitimately produce more than one row: a crash or a failed move
        // between the commit and the move leaves the file to be picked up again next poll.
        // That is the intended trade — a duplicate row is recoverable, a lost notice is not —
        // and a unique index here would turn it into a file that can never be moved and is
        // retried forever. Deciding two arrivals are the same document is review's job.
        builder.HasIndex(n => n.Sha256);

        // Filtered, so the many notices with no message id do not collide with each other.
        builder.HasIndex(n => n.EmailMessageId)
            .IsUnique()
            .HasFilter("[EmailMessageId] IS NOT NULL");
    }
}
