using System.Diagnostics;
using OmsLoan.Api.Ops;
using static OmsLoan.Api.Tests.Ops.OpsTestConfiguration;

namespace OmsLoan.Api.Tests.Ops;

public class OpsDatabaseTests
{
    private sealed class FailingProbe : IOpsDatabaseProbe
    {
        public Task<string> ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("connection refused");
    }

    private sealed class HangingProbe : IOpsDatabaseProbe
    {
        public async Task<string> ProbeAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return OpsDatabaseState.Healthy;
        }
    }

    private static Dictionary<string, string?> Connected(string? provider = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OmsLoan"] = "Host=localhost;Port=5432;Database=omsloan;Username=omsloan",
        };

        if (provider is not null)
        {
            settings["Database:Provider"] = provider;
        }

        return settings;
    }

    [Fact]
    public async Task PostgresProviderIsLabelledPostgres()
    {
        var status = await Build(Configuration(Connected("Postgres")));

        Assert.Equal(OpsStatusFactory.PostgresLabel, status.Database.Provider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SqlServer")]
    public async Task BlankOrSqlServerProviderIsLabelledSqlServer(string? provider)
    {
        var status = await Build(Configuration(Connected(provider)));

        Assert.Equal(OpsStatusFactory.SqlServerLabel, status.Database.Provider);
    }

    [Fact]
    public async Task NoConnectionStringReadsNotConfigured()
    {
        var status = await Build(Configuration());

        Assert.Equal(OpsDatabaseState.NotConfigured, status.Database.Status);
        Assert.Null(status.Database.Endpoint);
    }

    [Fact]
    public async Task AConfiguredDatabaseWithAWorkingProbeReadsHealthy()
    {
        var status = await Build(Configuration(Connected("Postgres")));

        Assert.Equal(OpsDatabaseState.Healthy, status.Database.Status);
        Assert.Equal("localhost:5432", status.Database.Endpoint);
    }

    [Fact]
    public async Task AFailedProbeReadsUnhealthy()
    {
        var status = await Build(Configuration(Connected("Postgres")), new FailingProbe());

        Assert.Equal(OpsDatabaseState.Unhealthy, status.Database.Status);
    }

    [Fact]
    public async Task AProbeThatHangsIsGivenUpOnAfterTheTimeout()
    {
        var stopwatch = Stopwatch.StartNew();

        var status = await Build(
            Configuration(Connected("Postgres")),
            new HangingProbe(),
            options: new OpsOptions { ProbeTimeoutMilliseconds = 50 });

        stopwatch.Stop();

        Assert.Equal(OpsDatabaseState.Unhealthy, status.Database.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AnUnusableConfiguredTimeoutStillProducesAStatus(int configured)
    {
        var status = await Build(
            Configuration(Connected("Postgres")),
            options: new OpsOptions { ProbeTimeoutMilliseconds = configured });

        Assert.Equal(OpsDatabaseState.Healthy, status.Database.Status);
    }

    [Fact]
    public async Task ASqlServerEndpointShowsTheServerWithoutAnInventedPort()
    {
        var status = await Build(Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OmsLoan"] = "Server=sql01;Database=OmsLoan;Integrated Security=true",
            ["Database:Provider"] = "SqlServer",
        }));

        Assert.Equal("sql01", status.Database.Endpoint);
    }

    [Fact]
    public void AnUnreadableConnectionStringHasNoEndpointRatherThanAGuess()
    {
        Assert.Null(OpsConnectionEndpoint.Describe("Database=omsloan;Username=omsloan", "Postgres"));
        Assert.Null(OpsConnectionEndpoint.Describe(null, "Postgres"));
    }
}
