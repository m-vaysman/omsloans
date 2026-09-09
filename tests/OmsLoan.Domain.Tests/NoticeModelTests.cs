using Microsoft.EntityFrameworkCore;
using OmsLoan.Domain;

namespace OmsLoan.Domain.Tests;

/// <summary>
/// Mapping of <see cref="Notice"/> — the dedup indexes and the provenance columns.
/// </summary>
public class NoticeModelTests
{
    [Fact]
    public void Notice_MapsToNoticesTable()
    {
        Assert.Equal("Notices", DomainModel.Entity<Notice>().GetTableName());
    }

    [Fact]
    public void Content_IsVarbinaryMax_AndRequired()
    {
        var content = DomainModel.Property<Notice>(nameof(Notice.Content));

        // The original PDF is the evidence a reviewer compares against and the input to
        // any reprocessing, so it is stored whole rather than as a parsed derivative.
        Assert.Equal("varbinary(max)", content.GetColumnType());
        Assert.False(content.IsNullable);
    }

    [Fact]
    public void Sha256_IsFixedWidthAsciiOfDigestLength()
    {
        var sha = DomainModel.Property<Notice>(nameof(Notice.Sha256));

        Assert.Equal(64, sha.GetMaxLength());
        Assert.False(sha.IsUnicode());
        Assert.True(sha.IsFixedLength());
        Assert.False(sha.IsNullable);
    }

    [Fact]
    public void Sha256_IsIndexed_ButNotUniquely_SoTheSameDocumentCanArriveTwice()
    {
        var index = DomainModel.Index<Notice>(nameof(Notice.Sha256));

        // Not unique, deliberately. Ingestion records a notice and only then moves the file,
        // so a crash or a failed move between the two leaves the file to be picked up and
        // recorded again. A duplicate row is recoverable; a file that can never be moved
        // because its insert always throws is not. Identifying two arrivals as the same
        // document is review's work, and this index is what makes that grouping cheap.
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void EmailMessageId_UniqueIndex_IsFilteredToNonNull()
    {
        var index = DomainModel.Index<Notice>(nameof(Notice.EmailMessageId));

        Assert.True(index.IsUnique);

        // Without the filter, SQL Server treats NULLs as equal for uniqueness and the
        // second folder-ingested notice would collide with the first. The filter is what
        // lets folder and upload ingestion leave the column null instead of inventing one.
        var filter = index.GetFilter();
        Assert.False(string.IsNullOrWhiteSpace(filter));
        Assert.Contains("EmailMessageId", filter!, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", filter!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_IsPersistedAsString_NotAsAnOrdinal()
    {
        var status = DomainModel.Property<Notice>(nameof(Notice.Status));

        // Still an enum in code — the conversion is a storage concern, not an API one.
        Assert.Equal(typeof(NoticeStatus), status.ClrType);

        // Stored as text so reordering or inserting a NoticeStatus member cannot silently
        // reinterpret existing rows, and so the table is legible in a query window.
        // Asserting the column type rather than the value converter: HasConversion<string>()
        // resolves at type-mapping time and does not surface via GetValueConverter().
        Assert.Equal("nvarchar(32)", status.GetColumnType());
        Assert.Equal(32, status.GetMaxLength());
        Assert.False(status.IsNullable);
    }

    [Fact]
    public void ReceivedAtUtc_IsRequired_AndSentAtUtcIsNot()
    {
        // Received is always known. Sent comes off the mail envelope and is genuinely
        // absent for folder and upload ingestion — it is not derivable from received.
        Assert.False(DomainModel.Property<Notice>(nameof(Notice.ReceivedAtUtc)).IsNullable);
        Assert.True(DomainModel.Property<Notice>(nameof(Notice.SentAtUtc)).IsNullable);
    }

    [Fact]
    public void SenderAndEmailMessageId_AreOptional()
    {
        Assert.True(DomainModel.Property<Notice>(nameof(Notice.Sender)).IsNullable);
        Assert.True(DomainModel.Property<Notice>(nameof(Notice.EmailMessageId)).IsNullable);
    }

    [Fact]
    public void Sender_AccommodatesAFullLengthEmailAddress()
    {
        Assert.Equal(320, DomainModel.Property<Notice>(nameof(Notice.Sender)).GetMaxLength());
    }
}
