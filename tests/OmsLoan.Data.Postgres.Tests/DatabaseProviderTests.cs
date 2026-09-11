using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres.Tests;

public class DatabaseProviderTests
{
    private const string PostgresConnection = "Host=localhost;Port=5432;Database=omsloan;Username=omsloan";

    private const string SqlServerConnection = @"Server=(localdb)\MSSQLLocalDB;Database=OmsLoan;Trusted_Connection=True";

    private static OmsLoanDbContext Resolve(string? provider, string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseProvider.ConfigurationKey] = provider,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOmsLoanDatabase(configuration, connectionString);

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<OmsLoanDbContext>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SqlServer")]
    [InlineData("sqlserver")]
    public void SqlServerIsUsedUnlessPostgresIsConfigured(string? provider)
    {
        using var context = Resolve(provider, SqlServerConnection);

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("postgres")]
    public void PostgresIsUsedWhenConfigured(string provider)
    {
        using var context = Resolve(provider, PostgresConnection);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void AnUnknownProviderIsRefusedNamingTheSetting()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("MySql", PostgresConnection));

        Assert.Contains(DatabaseProvider.ConfigurationKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains("MySql", ex.Message, StringComparison.Ordinal);
    }
}
