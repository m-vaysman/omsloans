namespace OmsLoan.Api.Ops;

public sealed class OpsOptions
{
    public const string SectionName = "Ops";

    // Default 2s so a down database cannot stall the 5s poll.
    public const int DefaultProbeTimeoutMilliseconds = 2000;

    public const int MaxProbeTimeoutMilliseconds = 60000;

    public string WorkerPath { get; set; } = @"G:\Services\OmsLoan";

    public string ApiPath { get; set; } = @"G:\Services\OmsLoanApi";

    // Phase 2 tails this. Unused until the deploy workflows write the file.
    public string DeployHistoryPath { get; set; } = @"C:\ProgramData\OmsLoan\deploy-history.ndjson";

    public int ProbeTimeoutMilliseconds { get; set; } = DefaultProbeTimeoutMilliseconds;

    public int MaxLogEntries { get; set; } = 50;

    public int MaxMessageLength { get; set; } = 300;

    // 0, negative, or above 60s must not 500 the page whose job is diagnosing config.
    public int ProbeTimeout() =>
        ProbeTimeoutMilliseconds is > 0 and <= MaxProbeTimeoutMilliseconds
            ? ProbeTimeoutMilliseconds
            : DefaultProbeTimeoutMilliseconds;
}
