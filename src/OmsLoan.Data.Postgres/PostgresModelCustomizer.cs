using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres;

/// <summary>
/// Postgres-only model seam. Runs the shared Domain configuration first, then overrides the
/// four mappings that are valid on SQL Server and illegal or wrong on Postgres. Registered via
/// ReplaceService&lt;IModelCustomizer&gt; so OmsLoanDbContext and the entity configs stay untouched.
/// </summary>
public sealed class PostgresModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    public const string DateColumnType = "date";

    // Stamp Kind=Utc without shifting ticks. Npgsql refuses a DateTime whose Kind is not UTC;
    // the upload form binds sentAtUtc without a Z, which would fail on Postgres only. SQL Server
    // stores the same wall-clock value today — SpecifyKind keeps that contract.
    public static readonly ValueConverter<DateTime, DateTime> UtcKind = new(
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // SQL Server: varbinary(max). Postgres has no varbinary — PDF bytes are bytea.
        modelBuilder.Entity<Notice>()
            .Property(n => n.Content)
            .HasColumnType("bytea");

        // SQL Server filter uses [brackets]; Postgres needs "double quotes" or the index will
        // not create. Filtered, not unique — same as NoticeConfiguration: a crash between
        // record and ack must be allowed to re-insert.
        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.EmailMessageId)
            .HasFilter("\"EmailMessageId\" IS NOT NULL");

        // SQL Server: nvarchar(max). Postgres: text.
        modelBuilder.Entity<Extraction>()
            .Property(e => e.RawJson)
            .HasColumnType("text");

        // Calendar date, not a timestamp. Must stay off the UtcKind stamp below — date has no Kind.
        modelBuilder.Entity<ExtractedField>()
            .Property(f => f.DateValue)
            .HasColumnType(DateColumnType);

        var timestamps = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                && property.GetColumnType() != DateColumnType)
            .ToList();

        foreach (var property in timestamps)
        {
            property.SetValueConverter(UtcKind);
        }
    }
}
