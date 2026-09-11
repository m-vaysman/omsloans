using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres.Design;

public sealed class PostgresDbContextFactory : IDesignTimeDbContextFactory<OmsLoanDbContext>
{
    public const string ConnectionStringVariable = "OMSLOAN_POSTGRES_CONNECTION";

    private const string LocalFallback = "Host=localhost;Port=5432;Database=omsloan;Username=omsloan";

    public OmsLoanDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? LocalFallback;

        var options = new DbContextOptionsBuilder<OmsLoanDbContext>()
            .UseOmsLoanPostgres(connectionString)
            .Options;

        return new OmsLoanDbContext(options);
    }
}
