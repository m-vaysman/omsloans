namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>One PDF attachment, with the provenance from the message that carried it.</summary>
/// <param name="Content">The attachment bytes, verbatim.</param>
/// <param name="FileName">Attachment name, for logging.</param>
public sealed record MailAttachment(byte[] Content, string FileName);

/// <summary>
/// A message worth ingesting, reduced to what a notice needs.
/// </summary>
/// <remarks>
/// The Graph SDK's <c>Message</c> is not used past the client boundary. Ingestion needs four
/// facts, and taking them here keeps the logic testable without standing up Graph — which is
/// the difference between testing the ordering rule and not testing it at all.
/// </remarks>
/// <param name="Id">Graph message id. Recorded on the notice as its provenance.</param>
/// <param name="Sender">The actual sender address from the envelope.</param>
/// <param name="SentAtUtc">
/// When the agent bank sent it, from the envelope. This is the one ingestion path where that
/// is genuinely known — the folder and upload paths leave it null rather than invent it.
/// </param>
/// <param name="Attachments">Every PDF attachment. Non-PDF attachments never reach here.</param>
public sealed record MailboxMessage(
    string Id,
    string? Sender,
    DateTime? SentAtUtc,
    IReadOnlyList<MailAttachment> Attachments);
