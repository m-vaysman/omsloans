namespace OmsLoan.Api;

/// <summary>
/// Identity of the Windows Service, in one place so the host, Event Log source, and install
/// scripts cannot drift apart.
/// </summary>
/// <remarks>
/// Parallel to <c>OmsLoan.Worker.ServiceMetadata</c> and deliberately not shared. The two
/// services install, start, and recover independently
/// (<see href="../../docs/decisions/0002-windows-service-over-desktop.md">ADR 0002</see>),
/// so they need distinct names; a shared type would invite renaming one on behalf of the other.
///
/// Install scripts read these values rather than repeating them: a service registered under
/// one name and logging under another is painful to trace.
/// </remarks>
public static class ServiceMetadata
{
    /// <summary>Name Windows Service Control Manager knows. Used by sc.exe and Get-Service.</summary>
    public const string ServiceName = "OmsLoanApi";

    /// <summary>The name shown in services.msc.</summary>
    public const string DisplayName = "OmsLoan Notice Review API";

    public const string Description =
        "Self-hosted Kestrel process serving the notice review API and the React review UI. "
        + "Reads notices and extractions produced by the OmsLoan Notice Extraction Worker and "
        + "records reviewer corrections against them.";

    /// <summary>
    /// Event Log source. Registered by the install script — creating a source needs admin
    /// rights the service account is not granted.
    /// </summary>
    public const string EventLogSource = "OmsLoanApi";
}
