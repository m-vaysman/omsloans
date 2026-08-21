namespace OmsLoan.Domain;

/// <summary>
/// One attempt at reading a notice with one model and one prompt version.
/// </summary>
/// <remarks>
/// Rows are append-only. Reprocessing a notice inserts a new row and clears
/// <see cref="IsCurrent"/> on the previous one in the same transaction; an existing row
/// is never updated in place. That is what makes a model or prompt change measurable
/// against the same notice instead of being judged by impression.
/// </remarks>
public class Extraction
{
    public int ExtractionId { get; set; }

    public int NoticeId { get; set; }

    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// The provider response exactly as returned, persisted before any parsing is
    /// attempted. This is what makes a bad extraction diagnosable months later, so it is
    /// written even when parsing the response subsequently fails.
    /// </summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>The pinned provider model id that produced this row.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>The prompt revision that produced this row.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the extraction that current reads should use. Exactly one row per
    /// notice carries this flag; the reprocess path maintains that transactionally.
    /// </summary>
    public bool IsCurrent { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ExtractedField> Fields { get; set; } = new List<ExtractedField>();
}
