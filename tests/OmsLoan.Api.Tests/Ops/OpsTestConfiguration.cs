using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OmsLoan.Api.Ops;

namespace OmsLoan.Api.Tests.Ops;

internal static class OpsTestConfiguration
{
    public static readonly DateTimeOffset Now = new(2026, 9, 12, 14, 30, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IConfiguration Configuration(Dictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();

    public static Task<OpsStatus> Build(
        IConfiguration configuration,
        IOpsDatabaseProbe? databaseProbe = null,
        bool stub = true,
        OpsOptions? options = null) =>
        OpsStatusFactory.BuildAsync(
            options ?? new OpsOptions(),
            configuration,
            databaseProbe ?? StubOpsDatabaseProbe.Instance,
            Now,
            stub,
            CancellationToken.None);

    public static string Serialize(OpsStatus status) => JsonSerializer.Serialize(status, WebJson);

    // Walks up from the test host directory to OmsLoan.sln so the appsettings
    // bind test finds the shipped file.
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmsLoan.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
