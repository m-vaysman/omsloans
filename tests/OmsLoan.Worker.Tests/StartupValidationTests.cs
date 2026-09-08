using Microsoft.Extensions.Configuration;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// The startup gate. These assert the two things that matter operationally: a Worker missing
/// its database or its Graph credential must not reach the SCM as Running, and it must stop
/// in a way the SCM does not retry.
/// </summary>
public class StartupValidationTests
{
    private const string AnyConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=OmsLoan";

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static (string, string?)[] Complete() =>
    [
        (ConfigurationKeys.ConnectionStringKey, AnyConnectionString),
        ("Graph:TenantId", "tenant"),
        ("Graph:ClientId", "client"),
        ("Graph:ClientSecret", "secret"),
    ];

    [Fact]
    public void CompleteConfigurationStarts()
    {
        Assert.Empty(StartupValidation.MissingRequiredSettings(Configuration(Complete())));
    }

    [Theory]
    [InlineData("ConnectionStrings:OmsLoan", "ConnectionStrings__OmsLoan")]
    [InlineData("Graph:TenantId", "GRAPH_TENANT_ID")]
    [InlineData("Graph:ClientId", "GRAPH_CLIENT_ID")]
    [InlineData("Graph:ClientSecret", "GRAPH_CLIENT_SECRET")]
    public void AnySingleMissingRequiredSettingStopsStartup(string configurationKey, string variable)
    {
        var configuration = Configuration([.. Complete().Where(v => v.Item1 != configurationKey)]);

        var missing = Assert.Single(StartupValidation.MissingRequiredSettings(configuration));
        Assert.Equal(variable, missing.EnvironmentVariable);

        // The variable name has to be in the message: under the SCM this text is the only
        // record anybody gets of why the service is not running.
        Assert.Contains(variable, StartupValidation.BuildMessage([missing]), StringComparison.Ordinal);
    }

    /// <summary>
    /// A partial Graph credential is as fatal as none at all — a tenant with no secret is
    /// somebody's half-finished configuration, not a working client.
    /// </summary>
    [Fact]
    public void PartialGraphCredentialStopsStartup()
    {
        var configuration = Configuration(
            (ConfigurationKeys.ConnectionStringKey, AnyConnectionString),
            ("Graph:TenantId", "tenant"));

        Assert.Equal(
            new[] { "GRAPH_CLIENT_ID", "GRAPH_CLIENT_SECRET" },
            StartupValidation.MissingRequiredSettings(configuration)
                .Select(setting => setting.EnvironmentVariable));
    }

    /// <summary>
    /// One restart per problem is the failure mode this avoids: whoever is configuring the
    /// host should get the whole list at once.
    /// </summary>
    [Fact]
    public void EveryMissingSettingIsReportedTogether()
    {
        var missing = StartupValidation.MissingRequiredSettings(Configuration());
        var message = StartupValidation.BuildMessage(missing);

        Assert.Equal(ConfigurationKeys.RequiredSettings.Count, missing.Count);

        foreach (var setting in ConfigurationKeys.RequiredSettings)
        {
            Assert.Contains(setting.EnvironmentVariable, message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The committed placeholders are empty strings, so "present but blank" must not pass.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankValueDoesNotSatisfyARequiredSetting(string blank)
    {
        var values = Complete()
            .Select(v => v.Item1 == "Graph:ClientSecret" ? (v.Item1, (string?)blank) : v)
            .ToArray();

        var missing = Assert.Single(StartupValidation.MissingRequiredSettings(Configuration(values)));
        Assert.Equal("GRAPH_CLIENT_SECRET", missing.EnvironmentVariable);
    }

    /// <summary>
    /// The no-retry rule, stated as a test. Under the SCM the process must end with a zero
    /// exit code: a non-zero one is an error termination, which is the trigger for the
    /// failure actions the installer configures, and a missing environment variable retried
    /// at one, two and five minutes fails identically every time.
    /// </summary>
    [Fact]
    public void RunningAsAServiceExitsZeroSoTheScmDoesNotRestartIt()
    {
        Assert.Equal(0, StartupValidation.ExitCodeFor(isWindowsService: true));
    }

    /// <summary>
    /// From a console there is no SCM to mislead, and a developer or a CI step wants a
    /// failed exit status.
    /// </summary>
    [Fact]
    public void RunningFromAConsoleExitsNonZero()
    {
        Assert.NotEqual(0, StartupValidation.ExitCodeFor(isWindowsService: false));
        Assert.Equal(StartupValidation.ConfigurationErrorExitCode, StartupValidation.ExitCodeFor(false));
    }

    /// <summary>
    /// Provider API keys stay optional. Three providers sit behind one interface precisely so
    /// that any one of them will do, and a notice ingested but not yet extracted is a state
    /// reprocessing fixes — unlike a notice that was never collected or never recorded.
    /// </summary>
    [Fact]
    public void MissingProviderApiKeysDoNotStopStartup()
    {
        var configuration = Configuration(Complete());

        Assert.Empty(StartupValidation.MissingRequiredSettings(configuration));

        foreach (var provider in ConfigurationKeys.ProviderApiKeys)
        {
            Assert.Null(configuration[provider.ConfigurationKey]);
            Assert.DoesNotContain(ConfigurationKeys.RequiredSettings, required => required == provider);
        }
    }

    [Fact]
    public void RequiredSettingsAreTheDatabaseAndTheGraphCredential()
    {
        Assert.Equal(
            new[] { "ConnectionStrings__OmsLoan", "GRAPH_TENANT_ID", "GRAPH_CLIENT_ID", "GRAPH_CLIENT_SECRET" },
            ConfigurationKeys.RequiredSettings.Select(setting => setting.EnvironmentVariable));
    }
}
