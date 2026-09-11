using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.EventLog;

namespace OmsLoan.Worker;

/// <summary>
/// Log sinks that only exist on Windows.
/// </summary>
/// <remarks>
/// Event Log wiring lives in an attributed method rather than inline in Program.cs: the
/// platform analyser treats a lambda as its own call site, so an
/// <c>OperatingSystem.IsWindows()</c> guard around the call does not cover the callback, and
/// the attribute is not honoured on a local function. A real method with
/// <see cref="SupportedOSPlatformAttribute"/> is what it follows.
///
/// Runtime guard still matters — targets net8.0 (not net8.0-windows) to stay consistent with
/// the rest of src/.
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
