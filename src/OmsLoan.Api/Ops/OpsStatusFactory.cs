using OmsLoan.Data.Postgres;

namespace OmsLoan.Api.Ops;

public static class OpsStatusFactory
{
    public const string WorkerServiceName = "OmsLoanWorker";

    public const string WorkerDisplayName = "OmsLoan Notice Extraction Worker";

    public const string PostgresLabel = "Postgres";

    public const string SqlServerLabel = "SQL Server";

    private static readonly string[] SecretNames =
    [
        "ConnectionStrings__OmsLoan",
        "CLAUDE_API_KEY",
        "OPEN_API_KEY",
        "GROQ_API_KEY",
        "GRAPH_TENANT_ID",
        "GRAPH_CLIENT_ID",
        "GRAPH_CLIENT_SECRET",
        "GRAPH_USER",
    ];

    public static async Task<OpsStatus> BuildAsync(
        IConfiguration configuration,
        IOpsDatabaseProbe databaseProbe,
        DateTimeOffset now,
        bool stub,
        CancellationToken cancellationToken)
    {
        var options = OpsOptions.FromConfiguration(configuration);
        var configuredProvider = configuration[DatabaseProvider.ConfigurationKey];
        var connectionString = configuration[ConfigurationKeys.ConnectionStringKey];

        var database = string.IsNullOrWhiteSpace(connectionString)
            ? new OpsDatabaseStatus(ProviderLabel(configuredProvider), OpsDatabaseState.NotConfigured, null)
            : new OpsDatabaseStatus(
                ProviderLabel(configuredProvider),
                await ProbeAsync(databaseProbe, options.ProbeTimeoutMilliseconds, cancellationToken),
                OpsConnectionEndpoint.Describe(connectionString, configuredProvider));

        var serviceState = stub ? OpsServiceState.Running : OpsServiceState.Unknown;

        var services = new[]
        {
            new OpsServiceStatus(WorkerServiceName, WorkerDisplayName, serviceState, options.WorkerPath),
            new OpsServiceStatus(
                ServiceMetadata.ServiceName,
                ServiceMetadata.DisplayName,
                serviceState,
                options.ApiPath),
        };

        var secrets = SecretNames
            .Select(name => new OpsSecretPresence(name, Present(configuration, name)))
            .ToList();

        return new OpsStatus(services, database, EmptyLogs(), secrets, now, stub);
    }

    public static string ProviderLabel(string? configuredProvider) =>
        string.Equals(configuredProvider, DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase)
            ? PostgresLabel
            : SqlServerLabel;

    private static OpsLogSections EmptyLogs() => new([], [], []);

    private static bool Present(IConfiguration configuration, string environmentVariableName) =>
        !string.IsNullOrWhiteSpace(
            configuration[environmentVariableName.Replace("__", ":", StringComparison.Ordinal)]);

    private static async Task<string> ProbeAsync(
        IOpsDatabaseProbe databaseProbe,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(timeoutMilliseconds);

        try
        {
            return await databaseProbe
                .ProbeAsync(probeCancellation.Token)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            return OpsDatabaseState.Unhealthy;
        }
    }
}
