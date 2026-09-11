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

        // Not unique. Ingestion records every arrival and only then moves the file, so the same
        // document can produce more than one row: a crash between commit and move leaves the
        // file for the next poll. A duplicate row is recoverable; a lost notice is not. A unique
        // index here would turn that into a file that can never be moved and is retried forever.
        builder.HasIndex(n => n.Sha256);

        // Filtered so null message ids do not all collide. Not unique for the same reason as
        // Sha256: mailbox ingestion records a notice and only then marks the message read, and
        // those two are not atomic. A crash between them re-reads and re-records; a unique index
        // would make that insert always throw and the mailbox reprocess forever.
        // One message can also carry several PDF attachments — each its own notice — so the same
        // message id legitimately appears more than once.
        builder.HasIndex(n => n.EmailMessageId)
            .HasFilter("[EmailMessageId] IS NOT NULL");
    }
}
