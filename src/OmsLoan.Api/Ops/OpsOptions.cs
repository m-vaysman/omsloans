namespace OmsLoan.Api.Ops;

public sealed class OpsOptions
{
    public const string SectionName = "Ops";

    public string WorkerPath { get; set; } = @"G:\Services\OmsLoan";

    public string ApiPath { get; set; } = @"G:\Services\OmsLoanApi";

    public string DeployHistoryPath { get; set; } = @"C:\ProgramData\OmsLoan\deploy-history.ndjson";

    public int ProbeTimeoutMilliseconds { get; set; } = 2000;

    public int MaxLogEntries { get; set; } = 50;

    public int MaxMessageLength { get; set; } = 300;

    public static OpsOptions FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<OpsOptions>() ?? new OpsOptions();
}
