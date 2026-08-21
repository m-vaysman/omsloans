using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OmsLoan.Domain;

/// <summary>
/// Single place the SQL Server options are chosen, so the Worker, the Api, and the
/// design-time tooling cannot drift apart on them.
/// </summary>
public static class OmsLoanDbContextRegistration
{
    /// <summary>
    /// SQL Server 2019. The provider otherwise assumes a newer level and can emit
    /// constructs the target server will reject at query time, which a migration that
    /// applies cleanly would not reveal.
    /// </summary>
    public const int SqlServerCompatibilityLevel = 150;

    /// <summary>Environment variable holding the connection string. See SETUP.md.</summary>
    public const string ConnectionStringVariable = "OMSLOAN_CONNECTION";

    public static IServiceCollection AddOmsLoanDbContext(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddDbContext<OmsLoanDbContext>(
            options => options.UseOmsLoanSqlServer(connectionString));
    }

    public static DbContextOptionsBuilder UseOmsLoanSqlServer(
        this DbContextOptionsBuilder options,
        string connectionString)
    {
        return options.UseSqlServer(
            connectionString,
            sqlServer => sqlServer.UseCompatibilityLevel(SqlServerCompatibilityLevel));
    }

    /// <summary>
    /// Generic overload, mirroring the shape of <c>UseSqlServer</c> itself, so callers
    /// building a typed <see cref="DbContextOptionsBuilder{TContext}"/> keep the type
    /// through to <c>.Options</c>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseOmsLoanSqlServer<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        string connectionString)
        where TContext : DbContext
    {
        UseOmsLoanSqlServer((DbContextOptionsBuilder)options, connectionString);
        return options;
    }
}
