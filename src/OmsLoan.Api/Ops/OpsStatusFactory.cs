using OmsLoan.Data.Postgres;

namespace OmsLoan.Api.Ops;

public static class OpsStatusFactory
{
    public const string WorkerServiceName = "OmsLoanWorker";

    public const string WorkerDisplayName = "OmsLoan Notice Extraction Worker";

    public const string PostgresLabel = "Postgres";

    public const string SqlServerLabel = "SQL Server";

    // Names only. Presence is reported; values never enter the payload.
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
        OpsOptions options,
        IConfiguration configuration,
        IOpsDatabaseProbe databaseProbe,
        DateTimeOffset now,
        bool stub,
        CancellationToken cancellationToken)
    {
        var configuredProvider = configuration[DatabaseProvider.ConfigurationKey];
        var connectionString = configuration[ConfigurationKeys.ConnectionStringKey];

        // No connection string: NotConfigured, no probe. Probing an empty
        // string would invent a health.
        var database = string.IsNullOrWhiteSpace(connectionString)
            ? new OpsDatabaseStatus(ProviderLabel(configuredProvider), OpsDatabaseState.NotConfigured, null)
            : new OpsDatabaseStatus(
                ProviderLabel(configuredProvider),
                await ProbeAsync(databaseProbe, options.ProbeTimeout(), cancellationToken),
                OpsConnectionEndpoint.Describe(connectionString, configuredProvider));

        // Phase 1 has no service query. Stub paints Running; without stub it
        // is Unknown, never invented. The page still marks stub cards "not measured".
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

        // Phase 1: logs stay empty. Event Log tails and deploy-history are phase 2.
        return new OpsStatus(services, database, EmptyLogs(), secrets, now, stub);
    }

    // Only the exact Postgres value flips the label. Blank or SqlServer → "SQL Server".
    public static string ProviderLabel(string? configuredProvider) =>
        string.Equals(configuredProvider, DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase)
            ? PostgresLabel
            : SqlServerLabel;

    private static OpsLogSections EmptyLogs() => new([], [], []);

    // Environment `__` to configuration `:`. ConnectionStrings__OmsLoan reads ConnectionStrings:OmsLoan.
    private static bool Present(IConfiguration configuration, string environmentVariableName) =>
        !string.IsNullOrWhiteSpace(
            configuration[environmentVariableName.Replace("__", ":", StringComparison.Ordinal)]);

    // Timeout (default 2s) so a down database never stalls the 5s poll.
    // CancelAfter for a probe that honors the token. WaitAsync for one that
    // does not. Request abort still throws. Timed-out or failed probe is
    // Unhealthy — no exception text, no stack frame.
    private static async Task<string> ProbeAsync(
        IOpsDatabaseProbe databaseProbe,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            probeCancellation.CancelAfter(timeoutMilliseconds);

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
