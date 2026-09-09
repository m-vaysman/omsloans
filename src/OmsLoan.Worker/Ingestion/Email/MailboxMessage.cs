namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>One PDF attachment, with the provenance from the message that carried it.</summary>
/// <param name="Content">The attachment bytes, verbatim.</param>
/// <param name="FileName">Attachment name, for logging.</param>
public sealed record MailAttachment(byte[] Content, string FileName);

/// <summary>
/// A message worth ingesting, reduced to what a notice needs.
/// </summary>
/// <remarks>
/// The Graph SDK's <c>Message</c> is not used past the client boundary. Ingestion needs a
/// handful of facts, and taking them here keeps the logic testable without standing up Graph
/// — which is the difference between testing the ordering rule and not testing it at all.
/// </remarks>
/// <param name="Id">Graph message id. Recorded on the notice as its provenance.</param>
/// <param name="Sender">The actual sender address from the envelope.</param>
/// <param name="SentAtUtc">
/// When the agent bank sent it, from the envelope. This is the one ingestion path where that
/// is genuinely known — the folder and upload paths leave it null rather than invent it.
/// </param>
/// <param name="Attachments">Every PDF attachment that could be read.</param>
/// <param name="AttachmentsIncomplete">
/// True when at least one attachment could not be enumerated or downloaded.
/// </param>
/// <remarks>
/// <see cref="AttachmentsIncomplete"/> exists to keep two very different situations apart:
/// a message that genuinely carries no PDF — a signature image, a plain reply — and one whose
/// PDF we failed to fetch. The first can be marked read and forgotten. The second must not be,
/// or the notice is lost with nothing anywhere to say it existed. Without the distinction,
/// both look like "no attachments" and both get marked read.
/// </remarks>
public sealed record MailboxMessage(
    string Id,
    string? Sender,
    DateTime? SentAtUtc,
    IReadOnlyList<MailAttachment> Attachments,
    bool AttachmentsIncomplete = false);
