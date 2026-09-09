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
    /// How many consecutive read failures a single file gets before it is moved to
    /// <c>failed/</c>.
    /// </summary>
    /// <remarks>
    /// This is the escape hatch for a file that can never be read. Without it the rule
    /// "nothing moves until it is recorded" has no exit, and one zero-byte or permanently
    /// locked file is retried and logged on every poll for as long as the service runs.
    ///
    /// It deliberately does <em>not</em> apply to database failures. A file that was read
    /// fine but could not be recorded stays where it is, for ever if need be: the watched
    /// folder is the queue while the database is unavailable, and moving those aside would
    /// turn an outage into lost notices.
    ///
    /// The default is generous because the common read failure is a file still being copied.
    /// Ten attempts at the default interval is five minutes, which covers a slow copy of a
    /// large PDF without leaving genuine rubbish in the folder all day.
    /// </remarks>
    public int MaxReadAttempts { get; set; } = 10;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);

    /// <summary>The archive root, falling back to the watched folder.</summary>
    public string EffectiveArchiveFolder =>
        string.IsNullOrWhiteSpace(ArchiveFolder) ? WatchedFolder : ArchiveFolder;
}
