using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OmsLoan.Domain.Design;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context without starting the Worker or the Api.
/// Migrations belong to the model; the tooling should not need either host — nor a real
/// database, for anything but <c>database update</c>.
/// </summary>
/// <remarks>
/// Design time only. Never registered in an application's service container.
/// </remarks>
public class OmsLoanDbContextFactory : IDesignTimeDbContextFactory<OmsLoanDbContext>
{
    /// <summary>
    /// LocalDB so <c>dotnet ef migrations add</c> works from a clean clone with no config.
    /// LocalDB 15.x is the SQL Server 2019 engine this schema targets. No credentials; overridden by
    /// <see cref="OmsLoanDbContextRegistration.ConnectionStringVariable"/>.
    /// </summary>
    private const string LocalDbFallback =
        @"Server=(localdb)\MSSQLLocalDB;Database=OmsLoan;Trusted_Connection=True;MultipleActiveResultSets=true";

    public OmsLoanDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                OmsLoanDbContextRegistration.ConnectionStringVariable)
            ?? LocalDbFallback;

        var options = new DbContextOptionsBuilder<OmsLoanDbContext>()
            .UseOmsLoanSqlServer(connectionString)
            .Options;

        return new OmsLoanDbContext(options);
    }
}
