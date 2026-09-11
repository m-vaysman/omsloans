namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// Everything folder ingestion needs to know, read from the <c>Ingestion</c> configuration
/// section.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Folder notices are dropped into. Required; the Worker will not start without it.</summary>
    public string WatchedFolder { get; set; } = string.Empty;

    /// <summary>
    /// Where <c>processed/</c> and <c>failed/</c> live. Empty means alongside the watched
    /// folder, which is the usual arrangement; a separate path is for the case where the drop
    /// folder is a share somebody else owns and we would rather not add subfolders to it.
    /// </summary>
    public string ArchiveFolder { get; set; } = string.Empty;

    /// <summary>
    /// How often the folder is scanned. Seconds rather than a TimeSpan so it reads naturally
    /// as an environment variable.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Consecutive read failures before a file moves to <c>failed/</c>.
    /// </summary>
    /// <remarks>
    /// Escape hatch for a file that can never be read. Without it, "nothing moves until
    /// recorded" has no exit — one locked file is retried every poll forever.
    ///
    /// Deliberately does <em>not</em> apply to database failures. A readable file that could
    /// not be recorded stays put: the watched folder is the queue while the database is down.
    /// Moving those aside would turn an outage into lost notices.
    ///
    /// Default is generous: common failure is a file still being copied. Ten attempts at the
    /// default interval is five minutes.
    /// </remarks>
    public int MaxReadAttempts { get; set; } = 10;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);

    /// <summary>The archive root, falling back to the watched folder.</summary>
    public string EffectiveArchiveFolder =>
        string.IsNullOrWhiteSpace(ArchiveFolder) ? WatchedFolder : ArchiveFolder;
}
