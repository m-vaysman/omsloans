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
    /// Lowercase hex SHA-256 of <see cref="Content"/>. Uniquely indexed, so the same
    /// document arriving twice — by email and again from the watched folder — is caught
    /// on content rather than on filename or message id.
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
    /// The mailbox message id, when the notice arrived by email. Null otherwise, under a
    /// filtered unique index, so folder and upload ingestion are not forced to invent one.
    /// </summary>
    public string? EmailMessageId { get; set; }

    public ICollection<Extraction> Extractions { get; set; } = new List<Extraction>();
}
