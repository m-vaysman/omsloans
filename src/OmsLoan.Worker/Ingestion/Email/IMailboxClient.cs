namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// The mailbox, reduced to the two things ingestion does with it.
/// </summary>
/// <remarks>
/// Interface so ingestion rules — record before mark, one notice per attachment, mark
/// failure — can be tested without tenant, network, or secret. Graph sits behind it.
///
/// Fetch has no side effects. Marking belongs to <see cref="EmailIngestion"/>, so a failed
/// mark cannot be mistaken for the mailbox being unreachable.
/// </remarks>
public interface IMailboxClient
{
    /// <summary>
    /// Inbox messages with attachments, oldest first, PDFs read.
    /// </summary>
    /// <remarks>
    /// Inbox is the queue — not "unread". A person opening the mailbox would dequeue by
    /// accident; reading is not "ingested".
    ///
    /// Oldest first so a backlog drains from the front.
    ///
    /// Messages with no PDF still return (empty list) so the caller can move them out;
    /// otherwise they are re-examined every poll forever.
    /// </remarks>
    Task<IReadOnlyList<MailboxMessage>> GetInboxMessagesAsync(
        int maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a message to the processed folder, which is what takes it out of the queue.
    /// </summary>
    /// <remarks>
    /// Moved, never deleted. The message is the original evidence and the only copy of the
    /// envelope the notice came from.
    /// </remarks>
    Task MoveToProcessedAsync(string messageId, CancellationToken cancellationToken);
}
