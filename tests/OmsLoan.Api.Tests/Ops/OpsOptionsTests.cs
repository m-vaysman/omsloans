using Microsoft.Extensions.Configuration;
using OmsLoan.Api.Ops;
using static OmsLoan.Api.Tests.Ops.OpsTestConfiguration;

namespace OmsLoan.Api.Tests.Ops;

public class OpsOptionsTests
{
    private static OpsOptions FromAppSettings()
    {
        var settingsPath = Path.Combine(RepositoryRoot(), "src", "OmsLoan.Api", "appsettings.json");

        Assert.True(File.Exists(settingsPath), settingsPath);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath)
            .Build();

        return configuration.GetSection(OpsOptions.SectionName).Get<OpsOptions>()!;
    }

    [Fact]
    public void TheAppSettingsSectionBindsToOpsOptions()
    {
        var options = FromAppSettings();

        Assert.NotNull(options);
        Assert.Equal(@"G:\Services\OmsLoan", options.WorkerPath);
        Assert.Equal(@"G:\Services\OmsLoanApi", options.ApiPath);
        Assert.Equal(@"C:\ProgramData\OmsLoan\deploy-history.ndjson", options.DeployHistoryPath);
    }

    [Fact]
    public void TheShippedSettingsMatchTheCodeDefaults()
    {
        var shipped = FromAppSettings();
        var defaults = new OpsOptions();

        Assert.Equal(defaults.WorkerPath, shipped.WorkerPath);
        Assert.Equal(defaults.ApiPath, shipped.ApiPath);
        Assert.Equal(defaults.DeployHistoryPath, shipped.DeployHistoryPath);
        Assert.Equal(defaults.ProbeTimeoutMilliseconds, shipped.ProbeTimeoutMilliseconds);
        Assert.Equal(defaults.MaxLogEntries, shipped.MaxLogEntries);
        Assert.Equal(defaults.MaxMessageLength, shipped.MaxMessageLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    [InlineData(OpsOptions.MaxProbeTimeoutMilliseconds + 1)]
    public void AnUnusableProbeTimeoutFallsBackToTheDefault(int configured)
    {
        var options = new OpsOptions { ProbeTimeoutMilliseconds = configured };

        Assert.Equal(OpsOptions.DefaultProbeTimeoutMilliseconds, options.ProbeTimeout());
    }

    [Fact]
    public void AUsableProbeTimeoutIsKept()
    {
        var options = new OpsOptions { ProbeTimeoutMilliseconds = 750 };

        Assert.Equal(750, options.ProbeTimeout());
    }
}
