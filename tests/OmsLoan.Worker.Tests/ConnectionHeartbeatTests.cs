using Microsoft.Extensions.Logging;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// The heartbeat reports transitions, not state. These assert that it says something the
/// moment contact is lost and the moment it returns, and stays quiet in between.
/// </summary>
public class ConnectionHeartbeatTests
{
    /// <summary>Captures what was logged, so the assertions are about output rather than internals.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly Exception Unreachable = new InvalidOperationException("host not found");

    private static (ConnectionHeartbeat Heartbeat, RecordingLogger Log, Clock Clock) Build()
    {
        var log = new RecordingLogger();
        var clock = new Clock(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        return (new ConnectionHeartbeat(log, "Mailbox notices@example.test", clock), log, clock);
    }

    [Fact]
    public void TheFirstSuccessIsReportedOnce()
    {
        var (heartbeat, log, _) = Build();

        heartbeat.RecordSuccess();
        heartbeat.RecordSuccess();
        heartbeat.RecordSuccess();

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("is reachable", entry.Message, StringComparison.Ordinal);
        Assert.True(heartbeat.IsUp);
    }

    /// <summary>
    /// The moment it goes. One warning, naming the failure.
    /// </summary>
    [Fact]
    public void TheFirstFailureIsReportedAsAWarning()
    {
        var (heartbeat, log, _) = Build();

        heartbeat.RecordSuccess();
        log.Entries.Clear();

        heartbeat.RecordFailure(Unreachable);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("unreachable", entry.Message, StringComparison.Ordinal);
        Assert.False(heartbeat.IsUp);
    }

    /// <summary>
    /// The point of the whole class. A mailbox down overnight at a one-minute poll would
    /// otherwise bury the first warning under hundreds of identical ones.
    /// </summary>
    [Fact]
    public void ContinuedFailureIsSilentUntilTheReminderInterval()
    {
        var (heartbeat, log, clock) = Build();

        heartbeat.RecordFailure(Unreachable);
        log.Entries.Clear();

        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            heartbeat.RecordFailure(Unreachable);
        }

        Assert.Empty(log.Entries);
    }

    /// <summary>
    /// But a long outage is not left as one line somebody has to scroll back three days for.
    /// </summary>
    [Fact]
    public void AProlongedOutageRepeatsItselfAtTheReminderInterval()
    {
        var (heartbeat, log, clock) = Build();

        heartbeat.RecordFailure(Unreachable);
        log.Entries.Clear();

        clock.Advance(ConnectionHeartbeat.ReminderInterval + TimeSpan.FromSeconds(1));
        heartbeat.RecordFailure(Unreachable);

        var entry = Assert.Single(log.Entries);
        Assert.Contains("still unreachable", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The moment it comes back — and how long it was out, because "4 minutes" and "9 hours"
    /// call for very different follow-up.
    /// </summary>
    [Fact]
    public void RecoveryIsReportedWithTheOutageDuration()
    {
        var (heartbeat, log, clock) = Build();

        heartbeat.RecordFailure(Unreachable);
        clock.Advance(TimeSpan.FromHours(9));
        log.Entries.Clear();

        heartbeat.RecordSuccess();

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("reachable again", entry.Message, StringComparison.Ordinal);
        Assert.Contains("9.0 hours", entry.Message, StringComparison.Ordinal);
        Assert.True(heartbeat.IsUp);
    }

    /// <summary>
    /// A recovered connection that stays up says nothing further — otherwise every poll for
    /// the rest of the day would log that things are fine.
    /// </summary>
    [Fact]
    public void SuccessAfterRecoveryIsSilent()
    {
        var (heartbeat, log, clock) = Build();

        heartbeat.RecordFailure(Unreachable);
        clock.Advance(TimeSpan.FromMinutes(5));
        heartbeat.RecordSuccess();
        log.Entries.Clear();

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            heartbeat.RecordSuccess();
        }

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ASecondOutageIsReportedAgain()
    {
        var (heartbeat, log, clock) = Build();

        heartbeat.RecordFailure(Unreachable);
        clock.Advance(TimeSpan.FromMinutes(2));
        heartbeat.RecordSuccess();
        log.Entries.Clear();

        clock.Advance(TimeSpan.FromMinutes(30));
        heartbeat.RecordFailure(Unreachable);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("unreachable", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DownForReportsHowLongItHasBeenOut()
    {
        var (heartbeat, _, clock) = Build();

        Assert.Null(heartbeat.DownFor);

        heartbeat.RecordFailure(Unreachable);
        clock.Advance(TimeSpan.FromMinutes(7));

        Assert.Equal(TimeSpan.FromMinutes(7), heartbeat.DownFor);
    }
}
