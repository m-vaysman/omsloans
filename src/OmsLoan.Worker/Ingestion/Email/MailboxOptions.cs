namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// Everything mailbox ingestion needs, read from the <c>Graph</c> configuration section.
/// </summary>
public sealed class MailboxOptions
{
    public const string SectionName = "Graph";

    /// <summary>Directory the app registration lives in.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application id of the registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret. Never logged, never committed.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The shared mailbox agent banks send to. An address, not a secret.
    /// </summary>
    /// <remarks>
    /// Supplied by <c>GRAPH_USER</c>, which is what the machines and
    /// <c>tools/GraphDaemonSmokeTest.linq</c> already call it. Note that on a development
    /// machine it is often set at <em>user</em> scope, which a Windows Service never sees —
    /// set it with <c>setx /M</c> on any host running the service. See
    /// docs/exchange-test-environment.md.
    /// </remarks>
    public string Mailbox { get; set; } = string.Empty;

    /// <summary>How often the mailbox is polled, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>Messages fetched per poll.</summary>
    /// <remarks>
    /// A page rather than everything. A mailbox that has been unattended for a week should
    /// not be drained in one request that times out halfway; unread messages that do not fit
    /// are simply picked up on the next poll, because unread is the queue.
    /// </remarks>
    public int MessagesPerPoll { get; set; } = 25;

    /// <summary>Turns mailbox ingestion off without removing the credentials.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
}
