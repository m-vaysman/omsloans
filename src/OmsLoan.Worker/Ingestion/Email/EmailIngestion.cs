using Microsoft.Extensions.Options;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// One pass over the shared mailbox: read each unread message, record PDF attachments, then
/// mark it read.
/// </summary>
/// <remarks>
/// <para>
/// Same rule as folder ingestion, move standing in for file move: <strong>a message leaves
/// the inbox only after its notices are committed</strong>. The mailbox is the queue; unread
/// means not yet ingested. If the database is down, messages stay unread and are picked up
/// later — an outage shows as a mailbox filling, not as notices that never existed.
/// </para>
/// <para>
/// Commit and move are not atomic: a crash after commit re-reads and records again.
/// At-least-once, the right way round. That is why <c>Notices.EmailMessageId</c> is indexed
/// but not unique — a unique index would throw forever and the message could never leave.
/// </para>
/// <para>
/// Only path with real provenance: sender and send time come from the envelope, so
/// <see cref="Notice.SentAtUtc"/> is known here. Folder ingestion only knows when the file
/// arrived.
/// </para>
/// </remarks>
public sealed class EmailIngestion
{
    private readonly IMailboxClient _mailbox;
    private readonly INoticeStore _store;
    private readonly MailboxOptions _options;
    private readonly ILogger<EmailIngestion> _logger;
    private readonly TimeProvider _time;

    public EmailIngestion(
        IMailboxClient mailbox,
        INoticeStore store,
        IOptions<MailboxOptions> options,
        ILogger<EmailIngestion> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;

        // There is no point building this at all without a mailbox: every poll would ask
        // Graph for the messages of an empty address and fail in a way that reads like a
        // connectivity problem. Failing here instead makes it a composition error, thrown
        // once, at the point the mistake actually is.
        //
        // Startup validation already refuses to run without Graph:Mailbox, so in the host
        // this is unreachable. It is here for everything that is not the host — a test, a
        // future tool, a second registration — where nothing has checked.
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.Mailbox, "options.Value.Mailbox");

        _mailbox = mailbox;
        _store = store;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;

        Heartbeat = new ConnectionHeartbeat(logger, $"Mailbox {_options.Mailbox}", _time);
    }

    /// <summary>
    /// Connectivity to the mailbox. Exposed so a health check can read it without waiting for
    /// a poll, and so tests can assert on transitions rather than on log text.
    /// </summary>
    public ConnectionHeartbeat Heartbeat { get; }

    /// <summary>
    /// Messages whose notices are committed but which could not be moved out of the inbox.
    /// </summary>
    /// <remarks>
    /// Without this, a move that fails <em>permanently</em> — the app registration
    /// holding Mail.Read rather than Mail.ReadWrite is the way that happens — re-records the
    /// same notices on every poll, for ever. Not an outage: a silent flood. At a one-minute
    /// interval a single stuck message is fourteen hundred duplicate notices a day, and the
    /// only sign is a warning nobody is reading.
    ///
    /// So a message recorded but not marked is remembered, and on later polls it is only
    /// retried for the mark, never recorded again. Duplicates are bounded to the one already
    /// accepted by at-least-once rather than growing without limit.
    ///
    /// In memory, so a restart re-records once. That is the same bound as any other crash
    /// between committing and marking, and the alternative — persisting it — would be a
    /// second queue to keep correct.
    /// </remarks>
    private readonly HashSet<string> _recordedButNotMoved = new(StringComparer.Ordinal);

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MailboxMessage> messages;

        // The fetch is the connectivity probe. There is no separate ping: a poll that returns
        // — even with nothing in it — is proof the tenant, the credential and the mailbox are
        // all working, and a poll that throws is the moment that stopped being true.
        try
        {
            messages = await _mailbox.GetInboxMessagesAsync(
                _options.MessagesPerPoll, cancellationToken);

            Heartbeat.RecordSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Credentials rejected, tenant unreachable, mailbox gone, throttled past the
            // retries. All the same from here: nothing was read, nothing is lost, try again
            // next poll. The heartbeat decides whether this is worth a log entry.
            Heartbeat.RecordFailure(ex);
            return;
        }

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await IngestAsync(message, cancellationToken);
        }
    }

    private async Task IngestAsync(MailboxMessage message, CancellationToken cancellationToken)
    {
        if (_recordedButNotMoved.Contains(message.Id))
        {
            // Already committed on an earlier poll; only the mark failed. Recording it again
            // would multiply notices for as long as whatever blocks the mark persists.
            _logger.LogInformation(
                "Message {MessageId} is already recorded; retrying only the move.", message.Id);

            await MoveAsync(message, recorded: 0, cancellationToken);
            return;
        }

        if (message.Attachments.Count == 0)
        {
            // Nothing to record. Whether the message can leave the queue turns entirely on
            // *why* there is nothing — and getting this wrong is how a notice disappears.
            if (message.AttachmentsIncomplete)
            {
                // We could not read what it carried. Leaving it in the inbox keeps it where a human
                // can see it; marking it read would file a notice we never got as handled,
                // with nothing anywhere to say it existed.
                _logger.LogWarning(
                    "Message {MessageId} from {Sender} has attachments that could not be read. "
                    + "Leaving it in the inbox rather than treating it as having none.",
                    message.Id,
                    message.Sender ?? "(unknown sender)");

                return;
            }

            // Genuinely no PDF — a plain reply, or a signature image. Marked read so it is not
            // re-examined on every poll for ever.
            _logger.LogInformation(
                "Message {MessageId} carries no PDF attachment. Moving it out of the inbox.", message.Id);

            await MoveAsync(message, 0, cancellationToken);
            return;
        }

        var recorded = 0;

        foreach (var attachment in message.Attachments)
        {
            // One notice per attachment. A single mail carrying three notices is three
            // separate documents to review, not one with the other two hidden inside it.
            var notice = NoticeContent.Create(
                attachment.Content,
                receivedAtUtc: _time.GetUtcNow().UtcDateTime,
                sender: message.Sender,
                sentAtUtc: message.SentAtUtc);

            notice.EmailMessageId = message.Id;

            try
            {
                await _store.AddAsync(notice, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Not recorded, so the message is not moved out of the inbox. It stays in the queue and
                // is retried, indefinitely if the database stays down.
                _logger.LogError(
                    ex,
                    "Could not record {FileName} (sha256 {Hash}) from message {MessageId}. "
                    + "Leaving the message unread to retry.",
                    attachment.FileName,
                    notice.Sha256,
                    message.Id);

                return;
            }

            recorded++;

            _logger.LogInformation(
                "Ingested {FileName} (sha256 {Hash}) from {Sender} as notice {NoticeId}.",
                attachment.FileName,
                notice.Sha256,
                message.Sender ?? "(unknown sender)",
                notice.NoticeId);
        }

        if (message.AttachmentsIncomplete)
        {
            // Some attachments were read and recorded, others could not be fetched. The
            // message stays in the inbox so the rest are retried; the ones already recorded will
            // arrive a second time, which is the accepted duplicate rather than a lost notice.
            _logger.LogWarning(
                "Message {MessageId} was recorded as {Count} notice(s) but some attachments "
                + "could not be read. Leaving it in the inbox to retry the rest.",
                message.Id,
                recorded);

            return;
        }

        // Every attachment is committed, so the message can leave the queue. A partial
        // failure above returned already, deliberately: moving after recording only
        // some of the attachments would lose the rest with no trace.
        await MoveAsync(message, recorded, cancellationToken);
    }

    /// <summary>
    /// Takes the message out of the queue.
    /// </summary>
    /// <remarks>
    /// A failure here is not a connectivity failure and must not be reported as one — the
    /// fetch already succeeded, so the mailbox is plainly reachable. It is logged and the
    /// message is left unread, which means it is ingested again next poll and its notices
    /// recorded a second time. At-least-once, and the reason EmailMessageId is not unique.
    /// </remarks>
    private async Task MoveAsync(MailboxMessage message, int recorded, CancellationToken cancellationToken)
    {
        try
        {
            await _mailbox.MoveToProcessedAsync(message.Id, cancellationToken);

            _recordedButNotMoved.Remove(message.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Error, not Warning. A mark that cannot be made is not a hiccup: the message
            // stays in the queue for ever and needs somebody to fix a permission. The usual
            // cause is the app registration holding Mail.Read where moving requires
            // Mail.ReadWrite, which no amount of retrying will resolve.
            var first = _recordedButNotMoved.Add(message.Id);

            if (first)
            {
                _logger.LogError(
                    ex,
                    "Message {MessageId} was recorded as {Count} notice(s) but could not be marked "
                    + "read, so it stays in the mailbox. Its notices will not be recorded again, "
                    + "but nothing else will leave the queue behind it until this is fixed. "
                    + "Mail.ReadWrite is required to move a message; Mail.Read is not enough.",
                    message.Id,
                    recorded);
            }
        }
    }
}
