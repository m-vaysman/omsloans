namespace OmsLoan.Worker;

/// <summary>
/// The startup gate: refuses to run when a required setting is absent, and does so in a way
/// the SCM will not retry.
/// </summary>
/// <remarks>
/// <para>
/// Two decisions here, and the second one is the subtle one.
/// </para>
/// <para>
/// <strong>Why refuse at all.</strong> Without a database the Worker cannot record a notice;
/// without the Graph credential it cannot collect one from the shared mailbox. Starting
/// anyway produces a service the SCM reports as Running, that looks healthy in every
/// monitor, and that silently ingests nothing — discovered when somebody asks why the review
/// queue is empty, typically much later. Failing at startup is louder and cheaper.
/// </para>
/// <para>
/// <strong>Why it does not throw.</strong> An unhandled exception kills the process without
/// a clean stop, which is exactly what the SCM classifies as an unexpected termination — the
/// trigger for the failure actions the installer configures. A missing environment variable
/// would then be retried at one, two and five minutes before the SCM gave up for the day,
/// and every one of those attempts would fail identically. Retrying is for conditions that
/// resolve on their own: a database still starting, a network not yet up. Configuration is
/// not one of them.
/// </para>
/// <para>
/// So this reports and returns, the host is never started, and the process ends normally.
/// The exit code is chosen by <see cref="ExitCodeFor"/>: zero under the SCM, so the stop
/// cannot be read as an error termination and no recovery action fires; non-zero in a
/// console, where a developer or a CI step legitimately wants a failed exit status.
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
    /// Writes the refusal to the log. Called after the startup banner, so whoever reads the
    /// log sees the full configuration picture immediately above the reason it stopped.
    /// </summary>
    /// <remarks>
    /// Critical rather than Error: the Event Log level filter in appsettings admits Warning
    /// and above, and this is the one message that must never be filtered out. Under the SCM
    /// it is the only record anybody gets.
    /// </remarks>
    public static void LogRefusalToStart(ILogger logger, IReadOnlyList<ConfiguredSetting> missing)
    {
        logger.LogCritical("{StartupFailure}", BuildMessage(missing));
    }

    /// <summary>
    /// The same refusal, for a problem that is not a missing setting — a watched folder that
    /// cannot be created, read or written. Same level, same wording, same clean stop, because
    /// from an operator's side it is the same situation: something has to be fixed on the
    /// host before this service can run, and restarting will not do it.
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
    /// Zero when running as a Windows Service, so the SCM sees an ordinary stop rather than
    /// an error termination and leaves the failure actions alone. Non-zero otherwise.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate and is the whole mechanism. A service that exits with an
    /// error, or dies without stopping cleanly, is a candidate for restart; one that stops
    /// normally is not. A misconfigured Worker should stay stopped until somebody sets the
    /// variable, and the Event Log entry — not a restart loop — is what tells them to.
    ///
    /// Genuine faults are unaffected: an exception thrown once the host is running still
    /// terminates the process unexpectedly and still earns the 1m / 2m / 5m backoff.
    /// </remarks>
    public static int ExitCodeFor(bool isWindowsService) =>
        isWindowsService ? 0 : ConfigurationErrorExitCode;

    /// <summary>
    /// Names every missing variable rather than stopping at the first. Somebody configuring
    /// a host wants one list, not three restarts each revealing the next problem.
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
