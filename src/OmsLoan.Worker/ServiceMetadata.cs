namespace OmsLoan.Worker;

/// <summary>
/// Identity of the Windows Service, in one place so the host, Event Log source, and install
/// scripts cannot drift apart.
/// </summary>
/// <remarks>
/// Install scripts read these values rather than repeating them: a service registered under
/// one name and logging under another is painful to trace.
/// </remarks>
public static class ServiceMetadata
{
    /// <summary>Name Windows Service Control Manager knows. Used by sc.exe and Get-Service.</summary>
    public const string ServiceName = "OmsLoanWorker";

    /// <summary>The name shown in services.msc.</summary>
    public const string DisplayName = "OmsLoan Notice Extraction Worker";

    public const string Description =
        "Ingests agent-bank loan notices from a watched folder and a shared mailbox, "
        + "extracts their economic data with an LLM provider, and stores each attempt for "
        + "human review.";

    /// <summary>
    /// Event Log source. Registered by the install script — creating a source needs admin
    /// rights the service account is not granted.
    /// </summary>
    public const string EventLogSource = "OmsLoanWorker";
}
