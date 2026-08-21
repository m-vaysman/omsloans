using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.EventLog;

namespace OmsLoan.Worker;

/// <summary>
/// Log sinks that only exist on Windows.
/// </summary>
/// <remarks>
/// The Event Log wiring lives in an attributed method rather than inline in Program.cs
/// because the platform analyser evaluates a lambda as its own call site: an
/// <c>OperatingSystem.IsWindows()</c> guard around the call does not reach the configuration
/// callback inside it, and the attribute is not honoured on a local function either. A real
/// method carrying <see cref="SupportedOSPlatformAttribute"/> is what the analyser follows.
///
/// The runtime guard is still the thing that matters — this project targets net8.0 rather
/// than net8.0-windows so it stays consistent with the rest of src/.
/// </remarks>
internal static class WorkerLogging
{
    [SupportedOSPlatform("windows")]
    public static ILoggingBuilder AddOmsLoanEventLog(this ILoggingBuilder logging)
    {
        return logging.AddEventLog(new EventLogSettings
        {
            SourceName = ServiceMetadata.EventLogSource,
            LogName = "Application",
        });
    }
}
