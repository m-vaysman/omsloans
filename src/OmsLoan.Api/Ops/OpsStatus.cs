namespace OmsLoan.Api.Ops;

public static class OpsServiceState
{
    public const string Running = "Running";

    public const string Stopped = "Stopped";

    public const string Unknown = "Unknown";
}

public static class OpsDatabaseState
{
    public const string Healthy = "Healthy";

    public const string Unhealthy = "Unhealthy";

    public const string NotConfigured = "NotConfigured";
}

public sealed record OpsStatus(
    IReadOnlyList<OpsServiceStatus> Services,
    OpsDatabaseStatus Database,
    OpsLogSections Logs,
    IReadOnlyList<OpsSecretPresence> Secrets,
    DateTimeOffset GeneratedAtUtc,
    bool Stub);

public sealed record OpsServiceStatus(string Name, string DisplayName, string Status, string Path);

public sealed record OpsDatabaseStatus(string Provider, string Status, string? Endpoint);

public sealed record OpsLogSections(
    IReadOnlyList<OpsLogEntry> Worker,
    IReadOnlyList<OpsLogEntry> Api,
    IReadOnlyList<OpsLogEntry> Deploy);

public sealed record OpsLogEntry(DateTimeOffset TimestampUtc, string Level, string Message);

public sealed record OpsSecretPresence(string Name, bool Present);
