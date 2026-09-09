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
        // Filtered, so the many notices with no message id do not all index together. Not
        // unique, for the same reason Sha256 is not: mailbox ingestion records a notice and
        // only then marks the message read, and those two are not atomic. A crash between
        // them means the message is read again and recorded again — and a unique index would
        // turn that retry into an insert that always throws, a message that can never be
        // marked read, and a mailbox that reprocesses it for ever.
        //
        // One message can also carry several PDF attachments, and each is its own notice, so
        // the same message id legitimately appears more than once.
        builder.HasIndex(n => n.EmailMessageId)
            .HasFilter("[EmailMessageId] IS NOT NULL");
    }
}
