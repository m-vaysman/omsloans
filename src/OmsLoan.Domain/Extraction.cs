namespace OmsLoan.Domain;

/// <summary>
/// One attempt at reading a notice with one model and one prompt version.
/// </summary>
/// <remarks>
/// Rows are append-only. Reprocessing inserts a new row and clears <see cref="IsCurrent"/> on
/// the previous one in the same transaction; an existing row is never updated in place. That
/// is what makes a model or prompt change measurable against the same notice.
/// </remarks>
public class Extraction
{
    public int ExtractionId { get; set; }

    public int NoticeId { get; set; }

    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Provider response exactly as returned, persisted before any parse. Written even when
    /// parsing later fails — that is what makes a bad extraction diagnosable months later.
    /// </summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>Pinned provider model id that produced this row.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Prompt revision that produced this row.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether current reads should use this row. Exactly one row per notice carries this;
    /// the reprocess path maintains that in one transaction.
    /// </summary>
    public bool IsCurrent { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ExtractedField> Fields { get; set; } = new List<ExtractedField>();
}
