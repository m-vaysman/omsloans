namespace OmsLoan.Worker;

/// <summary>
/// Startup gate: refuses to run when a required setting is absent, in a way Windows Service
/// Control Manager will not retry.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why refuse.</strong> Without a database the Worker cannot record a notice; without
/// Graph it cannot collect one. Starting anyway produces a service the manager reports as
/// Running that silently ingests nothing — discovered when the review queue stays empty.
/// Failing at startup is louder and cheaper.
/// </para>
/// <para>
/// <strong>Why it does not throw.</strong> An unhandled exception is an unexpected
/// termination — the trigger for the installer's failure actions. A missing variable would
/// then retry at 1m / 2m / 5m, each attempt failing identically. Retrying is for conditions
/// that resolve on their own. Configuration is not one of them.
/// </para>
/// <para>
/// So this reports and returns; the host never starts. <see cref="ExitCodeFor"/> chooses
/// zero under an installed Windows service (ordinary stop, no recovery) and non-zero under
/// Visual Studio / console / CI, where a failed exit status is wanted.
/// </para>
/// </remarks>
public static class StartupValidation
{
    /// <summary>Exit code used when the Worker refuses to start from a console.</summary>
    public const int ConfigurationErrorExitCode = 78;

    /// <summary>
    /// Required settings with no value, in declaration order. Empty means good to start.
    /// </summary>
    /// <remarks>
    /// Whitespace counts as absent, matching the committed placeholders in appsettings.json
    /// and the flat-variable source, so "present but blank" cannot pass the gate.
    /// </remarks>
    public static IReadOnlyList<ConfiguredSetting> MissingRequiredSettings(IConfiguration configuration) =>
    [
        .. ConfigurationKeys.RequiredSettings
            .Where(setting => string.IsNullOrWhiteSpace(configuration[setting.ConfigurationKey]))
    ];

    /// <summary>
    /// Writes the refusal after the startup banner, so the log shows what resolved above why
    /// it stopped.
    /// </summary>
    /// <remarks>
    /// Critical rather than Error: the Event Log filter admits Warning and above, and under
    /// an installed Windows service this is the only record anybody gets.
    /// </remarks>
    public static void LogRefusalToStart(ILogger logger, IReadOnlyList<ConfiguredSetting> missing)
    {
        logger.LogCritical("{StartupFailure}", BuildMessage(missing));
    }

    /// <summary>
    /// Same refusal for a watched folder that cannot be created, read, or written. Same clean
    /// stop: restarting will not fix host permissions.
    /// </summary>
    public static void LogRefusalToStart(ILogger logger, string reason)
    {
        logger.LogCritical(
            "{StartupFailure}",
            "OmsLoan worker is not starting: " + reason + "."
            + Environment.NewLine
            + "  Fix it on the host and start the service again. This is a configuration or "
            + "permissions problem, so the service stops rather than restarting: retrying would "
            + "fail identically. See docs/windows-service.md.");
    }

    /// <summary>
    /// Zero when running as an installed Windows service, so Windows Service Control Manager
    /// sees an ordinary stop and leaves failure actions alone. Non-zero under Visual Studio /
    /// console.
    /// </summary>
    /// <remarks>
    /// A service that exits with an error is a candidate for restart; one that stops normally
    /// is not. A misconfigured Worker should stay stopped until somebody sets the variable —
    /// the Event Log entry, not a restart loop, tells them.
    ///
    /// Genuine faults once the host is running still earn the 1m / 2m / 5m backoff.
    /// </remarks>
    public static int ExitCodeFor(bool isWindowsService) =>
        isWindowsService ? 0 : ConfigurationErrorExitCode;

    /// <summary>
    /// Names every missing variable. One list beats three restarts each revealing the next.
    /// </summary>
    public static string BuildMessage(IReadOnlyList<ConfiguredSetting> missing)
    {
        var detail = string.Join(
            Environment.NewLine,
            missing.Select(setting => $"    {setting.EnvironmentVariable}  ({setting.Purpose})"));

        return $"OmsLoan worker is not starting: {missing.Count} required setting(s) missing."
            + Environment.NewLine
            + detail
            + Environment.NewLine
            + "  Set them as machine environment variables, on the service's own environment block, "
            + "or in user-secrets for Development, then start the service again. The database and "
            + "the Graph credential are both required — without them the Worker can neither collect "
            + "a notice nor record one."
            + Environment.NewLine
            + "  This is a configuration problem, so the service stops rather than restarting: "
            + "retrying would fail identically. See docs/windows-service.md.";
    }
}
