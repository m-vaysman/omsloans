using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OmsLoan.Domain.Design;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context without starting the Worker or the Api.
/// Migrations are a property of the model, so the tooling should not need either host —
/// nor a real database, for anything but <c>database update</c>.
/// </summary>
/// <remarks>
/// Design time only; never registered in an application's service container.
/// </remarks>
public class OmsLoanDbContextFactory : IDesignTimeDbContextFactory<OmsLoanDbContext>
{
    /// <summary>
    /// LocalDB, so <c>dotnet ef migrations add</c> works from a clean clone with no
    /// configuration. LocalDB 15.x is the SQL Server 2019 engine, which is the version
    /// this schema targets. Contains no credentials and is overridden by
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
