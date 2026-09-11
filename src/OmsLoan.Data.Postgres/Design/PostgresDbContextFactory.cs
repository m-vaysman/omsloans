using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres.Design;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> against the Postgres migrations assembly.
/// Reads OMSLOAN_POSTGRES_CONNECTION so the password never lands in source; the local fallback
/// has no password and only works when Postgres trusts the client.
/// </summary>
public sealed class PostgresDbContextFactory : IDesignTimeDbContextFactory<OmsLoanDbContext>
{
    public const string ConnectionStringVariable = "OMSLOAN_POSTGRES_CONNECTION";

    // Host-only fallback for a local Docker Postgres with trust auth. Production passwords
    // come from OMSLOAN_POSTGRES_CONNECTION — never commit one here.
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
