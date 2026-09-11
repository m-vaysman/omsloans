namespace OmsLoan.Domain;

/// <summary>
/// What a notice's bytes are: PDF or not, their hash, and how a <see cref="Notice"/> is built.
/// </summary>
/// <remarks>
/// Shared by Worker and Api because neither may reference the other. A second implementation
/// is how a folder-ingested hash and an uploaded hash quietly stop matching.
/// </remarks>
public static class NoticeContent
{
    /// <summary>The first four bytes of every PDF.</summary>
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();

    /// <summary>
    /// Whether the bytes are a PDF, judged on the bytes themselves.
    /// </summary>
    /// <remarks>
    /// Magic number, not extension or content type. Both of those are supplied by the sender
    /// and are wrong often enough to matter. The bytes are the only part nobody gets wrong by accident.
    /// </remarks>
    public static bool IsPdf(ReadOnlySpan<byte> content) =>
        content.Length >= PdfMagic.Length && content[..PdfMagic.Length].SequenceEqual(PdfMagic);

    /// <summary>Lowercase hex SHA-256, matching <see cref="Notice.Sha256"/>.</summary>
    public static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Builds a notice from the bytes. No deduplication: every arrival is recorded; deciding
    /// two are the same document is review's work.
    /// </summary>
    /// <param name="sender">
    /// Who sent it when known. Null for folder ingestion, and null for an upload where the
    /// uploader did not say — unknown is recorded as null, not invented.
    /// </param>
    /// <param name="sentAtUtc">
    /// When the agent bank sent it when known. Never derived from a filesystem or upload
    /// timestamp: those are <paramref name="receivedAtUtc"/>, a different fact.
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
