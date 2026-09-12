using System.Text.Json;
using OmsLoan.Api.Ops;
using static OmsLoan.Api.Tests.Ops.OpsTestConfiguration;

namespace OmsLoan.Api.Tests.Ops;

public class OpsStatusContractTests
{
    [Fact]
    public async Task ThePayloadCarriesEveryFieldTheUiReads()
    {
        var status = await Build(Configuration());

        using var document = JsonDocument.Parse(Serialize(status));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("services", out var services));
        Assert.True(root.TryGetProperty("database", out var database));
        Assert.True(root.TryGetProperty("logs", out var logs));
        Assert.True(root.TryGetProperty("secrets", out _));
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        Assert.True(root.TryGetProperty("stub", out _));

        Assert.Equal(2, services.GetArrayLength());
        Assert.True(database.TryGetProperty("provider", out _));
        Assert.True(database.TryGetProperty("status", out _));
        Assert.True(database.TryGetProperty("endpoint", out _));
        Assert.True(logs.TryGetProperty("worker", out _));
        Assert.True(logs.TryGetProperty("api", out _));
        Assert.True(logs.TryGetProperty("deploy", out _));
    }

    [Fact]
    public async Task PropertyNamesAreCamelCaseAsTheUiExpects()
    {
        var json = Serialize(await Build(Configuration()));

        Assert.Contains("\"generatedAtUtc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GeneratedAtUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothServicesAreReportedWithTheConfiguredPaths()
    {
        var status = await Build(
            Configuration(),
            options: new OpsOptions { WorkerPath = @"D:\Services\Worker", ApiPath = @"D:\Services\Api" });

        Assert.Collection(
            status.Services,
            worker =>
            {
                Assert.Equal(OpsStatusFactory.WorkerServiceName, worker.Name);
                Assert.Equal(@"D:\Services\Worker", worker.Path);
            },
            api =>
            {
                Assert.Equal("OmsLoanApi", api.Name);
                Assert.Equal(@"D:\Services\Api", api.Path);
            });
    }

    [Fact]
    public async Task TheServicePathsDefaultToTheDeployLocations()
    {
        var status = await Build(Configuration());

        Assert.Equal(@"G:\Services\OmsLoan", status.Services[0].Path);
        Assert.Equal(@"G:\Services\OmsLoanApi", status.Services[1].Path);
    }

    [Fact]
    public async Task StatusValuesComeFromTheFixedSet()
    {
        var status = await Build(Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OmsLoan"] = "Host=localhost;Database=omsloan;Username=omsloan",
        }));

        string[] serviceStates = [OpsServiceState.Running, OpsServiceState.Stopped, OpsServiceState.Unknown];
        string[] databaseStates =
            [OpsDatabaseState.Healthy, OpsDatabaseState.Unhealthy, OpsDatabaseState.NotConfigured];

        Assert.All(status.Services, service => Assert.Contains(service.Status, serviceStates));
        Assert.Contains(status.Database.Status, databaseStates);
    }

    [Fact]
    public async Task StubIsTrueWhileNothingIsProbedForReal()
    {
        var status = await Build(Configuration());

        Assert.True(status.Stub);
        Assert.All(status.Services, service => Assert.Equal(OpsServiceState.Running, service.Status));
    }

    [Fact]
    public async Task WithoutStubDataServiceStateIsUnknownRatherThanInvented()
    {
        var status = await Build(Configuration(), stub: false);

        Assert.False(status.Stub);
        Assert.All(status.Services, service => Assert.Equal(OpsServiceState.Unknown, service.Status));
    }

    [Fact]
    public async Task TheGeneratedTimestampIsTheOneSupplied()
    {
        var status = await Build(Configuration());

        Assert.Equal(Now, status.GeneratedAtUtc);
    }

    [Fact]
    public async Task LogSectionsArePresentAndEmptyInPhaseOne()
    {
        var status = await Build(Configuration());

        Assert.Empty(status.Logs.Worker);
        Assert.Empty(status.Logs.Api);
        Assert.Empty(status.Logs.Deploy);
    }
}
