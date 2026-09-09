namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// The mailbox, reduced to the two things ingestion does with it.
/// </summary>
/// <remarks>
/// An interface so the ingestion rules — record before marking read, one notice per
/// attachment, what happens when the mark fails — can be tested without a tenant, a network
/// or a secret. The Graph implementation sits behind it and is the only part that cannot be
/// covered by a unit test.
///
/// The fetch has no side effects. It reads and returns; deciding what to mark read belongs to
/// <see cref="EmailIngestion"/>, where it is testable, and keeping it out of here means a
/// failed mark cannot be mistaken for the mailbox being unreachable.
/// </remarks>
public interface IMailboxClient
{
    /// <summary>
    /// Inbox messages carrying attachments, oldest first, with their PDF attachments read.
    /// </summary>
    /// <remarks>
    /// The inbox is the queue. Not "unread" — a person opening the mailbox to look at a
    /// notice would take it out of the queue by accident, and reading a message is not a
    /// statement about whether it has been ingested.
    ///
    /// Oldest first because notices are handled in the order they arrived, and because a
    /// backlog should drain from the front rather than the newest arrivals jumping it.
    ///
    /// A message with no PDF attachment is still returned, carrying an empty list. The caller
    /// needs to see it to move it out, or it is re-examined on every poll for ever.
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
