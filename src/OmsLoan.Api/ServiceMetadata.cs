namespace OmsLoan.Api;

/// <summary>
/// Identity of the Windows Service, in one place so the host, the Event Log source and the
/// install scripts cannot drift apart.
/// </summary>
/// <remarks>
/// Deliberately parallel to <c>OmsLoan.Worker.ServiceMetadata</c> and deliberately not
/// shared with it. The two services are installed, started and recovered independently —
/// see <see href="../../docs/decisions/0002-windows-service-over-desktop.md">ADR 0002</see>
/// — so they need distinct names, and a shared type would only invite one of them to be
/// renamed on behalf of the other.
///
/// The install scripts read these values rather than repeating them: a service registered
/// under one name and logging under another is painful to trace, and renaming in only one
/// of the two places is the easiest way to get there.
/// </remarks>
public static class ServiceMetadata
{
    /// <summary>The name the SCM knows the service by. Used by sc.exe and Get-Service.</summary>
    public const string ServiceName = "OmsLoanApi";

    /// <summary>The name shown in services.msc.</summary>
    public const string DisplayName = "OmsLoan Notice Review API";

    public const string Description =
        "Self-hosted Kestrel process serving the notice review API and the React review UI. "
        + "Reads notices and extractions produced by the OmsLoan Notice Extraction Worker and "
        + "records reviewer corrections against them.";

    /// <summary>
    /// Event Log source. Registered by the install script, because creating a source needs
    /// administrator rights the service account itself is not granted.
    /// </summary>
    public const string EventLogSource = "OmsLoanApi";
}
