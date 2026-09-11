namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>What is known about a connection.</summary>
public enum ConnectionState
{
    /// <summary>Nothing has been tried yet. Not the same as healthy.</summary>
    Unknown,

    /// <summary>The last attempt succeeded.</summary>
    Up,

    /// <summary>The last attempt failed.</summary>
    Down,
}

/// <summary>
/// Logs the moment a connection is lost and the moment it returns — nothing in between.
/// </summary>
/// <remarks>
/// <para>
/// About transitions, not state. Logging every failed poll buries the first one — the only
/// entry that matters. A mailbox down overnight at one-minute polls would otherwise produce
/// ~500 identical errors and scroll the alert away.
/// </para>
/// <para>
/// State change logs; continuation does not. Down is a Warning naming the error; up is
/// Information naming how long it was out — "back after 4 minutes" and "back after 9 hours"
/// need different follow-up.
/// </para>
/// <para>
/// Long outages get a reminder at <see cref="ReminderInterval"/> so Friday's outage is not a
/// single Monday line three days back. A heartbeat, not a repeat of the error.
/// </para>
/// <para>
/// Not thread-safe; driven from one polling loop.
/// </para>
/// </remarks>
public sealed class ConnectionHeartbeat(
    ILogger logger,
    string what,
    TimeProvider? timeProvider = null)
{
    /// <summary>How often an ongoing outage repeats itself in the log.</summary>
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private DateTimeOffset? _downSince;
    private DateTimeOffset _lastReminder;

    /// <summary>
    /// What is currently known. <see cref="ConnectionState.Unknown"/> until something has
    /// actually been tried.
    /// </summary>
    /// <remarks>
    /// Three states rather than a boolean, because a boolean has to lie at startup. Before
    /// the first poll nothing is known, and a health probe told "up" in that moment is told
    /// something nobody checked — from a signal whose only job is to be truthful about
    /// connectivity, "fine" from a cold start is the one answer it must never give.
    /// </remarks>
    public ConnectionState State { get; private set; } = ConnectionState.Unknown;

    /// <summary>How long it has been down, or null if it is up.</summary>
    public TimeSpan? DownFor => _downSince is null ? null : _time.GetUtcNow() - _downSince.Value;

    /// <summary>
    /// Records a successful contact. Logs only if this is a recovery, or the first success.
    /// </summary>
    public void RecordSuccess()
    {
        if (_downSince is not null)
        {
            var outage = _time.GetUtcNow() - _downSince.Value;
            _downSince = null;
            State = ConnectionState.Up;

            logger.LogInformation(
                "{What} is reachable again after {Outage}.", what, Describe(outage));

            return;
        }

        if (State == ConnectionState.Unknown)
        {
            // Said once, at startup, so the log records that contact was established at all.
            // Without it a mailbox that is reachable is indistinguishable from one nobody
            // ever tried.
            State = ConnectionState.Up;
            logger.LogInformation("{What} is reachable.", what);
        }
    }

    /// <summary>
    /// Records a failed contact. Logs the first failure, then only at the reminder interval.
    /// </summary>
    public void RecordFailure(Exception exception)
    {
        var now = _time.GetUtcNow();

        if (_downSince is null)
        {
            _downSince = now;
            _lastReminder = now;
            State = ConnectionState.Down;

            logger.LogWarning(
                exception, "{What} is unreachable. Ingestion is paused until it returns.", what);

            return;
        }

        if (now - _lastReminder < ReminderInterval)
        {
            return;
        }

        _lastReminder = now;

        logger.LogWarning(
            "{What} is still unreachable, {Outage} so far.", what, Describe(now - _downSince.Value));
    }

    /// <summary>Rounded to the largest sensible unit: nobody needs 4.0271 minutes.</summary>
    private static string Describe(TimeSpan outage) => outage switch
    {
        { TotalSeconds: < 90 } => $"{outage.TotalSeconds:N0} seconds",
        { TotalMinutes: < 90 } => $"{outage.TotalMinutes:N0} minutes",
        { TotalHours: < 48 } => $"{outage.TotalHours:N1} hours",
        _ => $"{outage.TotalDays:N1} days",
    };
}
