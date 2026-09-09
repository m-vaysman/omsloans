namespace OmsLoan.Domain;

/// <summary>
/// A notice received from an agent bank, stored as the original PDF bytes.
/// </summary>
/// <remarks>
/// The file bytes are kept verbatim rather than as a parsed derivative: they are the
/// evidence a reviewer compares against, and the input to any future reprocessing.
/// </remarks>
public class Notice
{
    public int NoticeId { get; set; }

    /// <summary>The original PDF, byte for byte as received.</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>
    /// Lowercase hex SHA-256 of <see cref="Content"/>. Indexed but <em>not</em> unique: the
    /// same document may legitimately appear more than once, and identifying those arrivals
    /// as the same document is review's job rather than ingestion's. The index is what makes
    /// that grouping cheap, and what lets extraction accuracy be compared across identical
    /// input.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Who sent it. Null for folder-ingested and manually uploaded notices, which have
    /// no sender, for the same reason <see cref="EmailMessageId"/> is nullable.
    /// </summary>
    public string? Sender { get; set; }

    /// <summary>
    /// When the agent bank sent it. Null when the notice did not arrive by email.
    /// Distinct from <see cref="ReceivedAtUtc"/> and not derivable from it.
    /// </summary>
    public DateTime? SentAtUtc { get; set; }

    /// <summary>When we ingested it. Always known.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    public NoticeStatus Status { get; set; }

    /// <summary>
    /// The mailbox message id, when the notice arrived by email. Null otherwise — under a
    /// filtered index, so folder and upload ingestion are not forced to invent one.
    /// </summary>
    /// <remarks>
    /// Indexed but not unique. One message can carry several PDF attachments and each is its
    /// own notice, and a message re-read after an interrupted run is recorded again rather
    /// than rejected — see <see cref="Sha256"/> for the same reasoning applied to content.
    /// </remarks>
    public string? EmailMessageId { get; set; }

    public ICollection<Extraction> Extractions { get; set; } = new List<Extraction>();
}
