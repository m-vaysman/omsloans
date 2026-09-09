namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// The mailbox, reduced to the two things ingestion does with it.
/// </summary>
/// <remarks>
/// An interface so the ingestion rules — record before marking read, one notice per
/// attachment, what happens when the mark fails — can be tested without a tenant, a network
/// or a secret. The Graph implementation sits behind it and is the only part that cannot be
/// covered by a unit test.
/// </remarks>
public interface IMailboxClient
{
    /// <summary>
    /// Unread messages carrying at least one PDF attachment, oldest first.
    /// </summary>
    /// <remarks>
    /// Oldest first because notices are processed in the order they arrived, and because a
    /// backlog should drain from the front rather than the newest arrivals jumping it.
    /// </remarks>
    Task<IReadOnlyList<MailboxMessage>> GetUnreadWithPdfAttachmentsAsync(
        int maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a message read, which is what takes it out of the queue.
    /// </summary>
    Task MarkReadAsync(string messageId, CancellationToken cancellationToken);
}
