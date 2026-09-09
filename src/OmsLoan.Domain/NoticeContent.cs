namespace OmsLoan.Domain;

/// <summary>
/// What a notice's bytes are: whether they are a PDF, what their hash is, and how a
/// <see cref="Notice"/> is built from them.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in either host because both need it and neither may reference the other
/// — the Worker ingests from a folder and a mailbox, the Api from an upload, and all three
/// must produce identical rows for identical bytes. A second implementation is how the hash
/// of a folder-ingested notice and an uploaded one quietly stop matching.
/// </para>
/// <para>
/// This is domain logic rather than hosting: what a notice's hash is, and what counts as a
/// PDF, are facts about the entity, not about how it arrived.
/// </para>
/// </remarks>
public static class NoticeContent
{
    /// <summary>The first four bytes of every PDF.</summary>
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();

    /// <summary>
    /// Whether the bytes are a PDF, judged on the bytes themselves.
    /// </summary>
    /// <remarks>
    /// The magic number, not the file extension or a declared content type. Both of those are
    /// supplied by whoever sent the file and are wrong often enough to matter — a mail client
    /// renaming an attachment, a browser guessing from an extension. The bytes are the only
    /// part nobody can get wrong by accident.
    /// </remarks>
    public static bool IsPdf(ReadOnlySpan<byte> content) =>
        content.Length >= PdfMagic.Length && content[..PdfMagic.Length].SequenceEqual(PdfMagic);

    /// <summary>Lowercase hex SHA-256, matching what is stored on <see cref="Notice.Sha256"/>.</summary>
    public static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Builds a notice from the bytes. No deduplication and no lookup: every arrival is
    /// recorded, and deciding two of them are the same document is review's work.
    /// </summary>
    /// <param name="sender">
    /// Who sent it, when that is known. Null for folder ingestion, and null for an upload
    /// where the uploader did not say — absent means unknown, and unknown is recorded as
    /// null rather than invented.
    /// </param>
    /// <param name="sentAtUtc">
    /// When the agent bank sent it, when that is known. Never derived from a filesystem or
    /// upload timestamp: those record when it reached us, which is
    /// <paramref name="receivedAtUtc"/> and a different fact.
    /// </param>
    public static Notice Create(
        byte[] content,
        DateTime receivedAtUtc,
        string? sender = null,
        DateTime? sentAtUtc = null) =>
        new()
        {
            Content = content,
            Sha256 = Sha256Hex(content),
            Sender = string.IsNullOrWhiteSpace(sender) ? null : sender.Trim(),
            SentAtUtc = sentAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            Status = NoticeStatus.Received,
        };
}
