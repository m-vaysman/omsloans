using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OmsLoan.Data.Postgres.Design;
using OmsLoan.Domain;
using OmsLoan.Domain.Design;

namespace OmsLoan.Data.Postgres.Tests;

public class PostgresModelTests
{
    private static OmsLoanDbContext Postgres() => new PostgresDbContextFactory().CreateDbContext([]);

    private static OmsLoanDbContext SqlServer() => new OmsLoanDbContextFactory().CreateDbContext([]);

    private static IModel DesignModel(DbContext context) => context.GetService<IDesignTimeModel>().Model;

    private static IReadOnlyProperty Property(DbContext context, Type entity, string name) =>
        DesignModel(context).FindEntityType(entity)!.FindProperty(name)!;

    private static string? EmailMessageIdFilter(DbContext context) =>
        DesignModel(context).FindEntityType(typeof(Notice))!
            .GetIndexes()
            .Single(index => index.Properties.Single().Name == nameof(Notice.EmailMessageId))
            .GetFilter();

    [Theory]
    [InlineData(typeof(Notice), nameof(Notice.Content), "bytea")]
    [InlineData(typeof(Extraction), nameof(Extraction.RawJson), "text")]
    [InlineData(typeof(ExtractedField), nameof(ExtractedField.DateValue), "date")]
    public void SqlServerOnlyColumnTypesHavePostgresEquivalents(Type entity, string property, string expected)
    {
        using var context = Postgres();

        Assert.Equal(expected, Property(context, entity, property).GetColumnType());
    }

    [Fact]
    public void TheEmailMessageIdFilterUsesPostgresQuoting()
    {
        using var context = Postgres();

        Assert.Equal("\"EmailMessageId\" IS NOT NULL", EmailMessageIdFilter(context));
    }

    [Fact]
    public void TheSqlServerModelIsUnchanged()
    {
        using var context = SqlServer();

        Assert.Equal("varbinary(max)", Property(context, typeof(Notice), nameof(Notice.Content)).GetColumnType());
        Assert.Equal("nvarchar(max)", Property(context, typeof(Extraction), nameof(Extraction.RawJson)).GetColumnType());
        Assert.Equal("[EmailMessageId] IS NOT NULL", EmailMessageIdFilter(context));
        Assert.Null(Property(context, typeof(Notice), nameof(Notice.ReceivedAtUtc)).GetValueConverter());
    }

    [Theory]
    [InlineData(typeof(Notice), nameof(Notice.ReceivedAtUtc))]
    [InlineData(typeof(Notice), nameof(Notice.SentAtUtc))]
    [InlineData(typeof(Extraction), nameof(Extraction.CreatedAtUtc))]
    [InlineData(typeof(ExtractedField), nameof(ExtractedField.CorrectedAtUtc))]
    public void TimestampsAreStampedUtc(Type entity, string property)
    {
        using var context = Postgres();

        Assert.Same(PostgresModelCustomizer.UtcKind, Property(context, entity, property).GetValueConverter());
    }

    [Fact]
    public void ADateColumnIsNotStampedAsATimestamp()
    {
        using var context = Postgres();

        Assert.Null(Property(context, typeof(ExtractedField), nameof(ExtractedField.DateValue)).GetValueConverter());
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void StampingUtcKeepsTheValue(DateTimeKind kind)
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 9, 11, 14, 30, 0), kind);

        var stored = (DateTime)PostgresModelCustomizer.UtcKind.ConvertToProvider(value)!;

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(value.Ticks, stored.Ticks);
    }

    [Fact]
    public void ThePostgresMigrationsAreUpToDateWithTheModel()
    {
        using var context = Postgres();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PostgresMigrationsAreSeparateFromSqlServerMigrations()
    {
        using var postgres = Postgres();
        using var sqlServer = SqlServer();

        var postgresMigrations = postgres.Database.GetMigrations().ToList();
        var sqlServerMigrations = sqlServer.Database.GetMigrations().ToList();

        Assert.NotEmpty(postgresMigrations);
        Assert.NotEmpty(sqlServerMigrations);
        Assert.Empty(postgresMigrations.Intersect(sqlServerMigrations));
    }
}
