using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres;

public sealed class PostgresModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    public const string DateColumnType = "date";

    public static readonly ValueConverter<DateTime, DateTime> UtcKind = new(
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.Entity<Notice>()
            .Property(n => n.Content)
            .HasColumnType("bytea");

        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.EmailMessageId)
            .HasFilter("\"EmailMessageId\" IS NOT NULL");

        modelBuilder.Entity<Extraction>()
            .Property(e => e.RawJson)
            .HasColumnType("text");

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
