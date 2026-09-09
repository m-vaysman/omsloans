namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// Everything mailbox ingestion needs, read from the <c>Graph</c> configuration section.
/// </summary>
/// <remarks>
/// <c>init</c> rather than <c>set</c>. Configuration binding still works — <c>init</c> is a
/// compile-time restriction and the binder sets properties by reflection — but nothing can
/// reassign a value after the options are built. These are read on every poll by a singleton;
/// a mutable options object is one where a stray assignment changes the mailbox being polled
/// for the lifetime of the process, and nothing would say so.
/// </remarks>
public sealed class MailboxOptions
{
    public const string SectionName = "Graph";

    /// <summary>Directory the app registration lives in.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Application id of the registration.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Client secret. Never logged, never committed.</summary>
    public string ClientSecret { get; init; } = string.Empty;

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
    public string Mailbox { get; init; } = string.Empty;

    /// <summary>
    /// Mail folder ingested messages are moved into. Created if it does not exist.
    /// </summary>
    /// <remarks>
    /// A move rather than a delete, and never a delete: the message is the original evidence
    /// and the only copy of the envelope. It is also a move rather than only a read flag,
    /// because the read flag is not ours to rely on — somebody opening the mailbox to look at
    /// a notice would take it out of the queue by accident. Which folder a message is in is
    /// state only this Worker changes.
    ///
    /// The same shape as the watched folder's <c>processed\</c>: the queue is what is still
    /// in the inbox, and handling something means moving it out.
    /// </remarks>
    public string ProcessedFolder { get; init; } = "OmsLoan Ingested";

    /// <summary>How often the mailbox is polled, in seconds.</summary>
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>Messages fetched per poll.</summary>
    /// <remarks>
    /// A page rather than everything. A mailbox that has been unattended for a week should
    /// not be drained in one request that times out halfway; unread messages that do not fit
    /// are simply picked up on the next poll, because unread is the queue.
    /// </remarks>
    public int MessagesPerPoll { get; init; } = 25;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
}
