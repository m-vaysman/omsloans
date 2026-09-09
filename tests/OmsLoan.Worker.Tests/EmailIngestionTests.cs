using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// Mailbox ingestion, and above all the ordering rule: a message is never moved until
/// its notices are committed.
/// </summary>
/// <remarks>
/// Both the mailbox and the store are fakes that fail on demand. "The message stays in the inbox
/// when the database is down" and "the mailbox being unreachable loses nothing" are the two
/// behaviours the design rests on, and neither is arrangeable against a real tenant.
/// </remarks>
public class EmailIngestionTests
{
    private readonly FakeMailbox _mailbox = new();
    private readonly FakeStore _store = new();

    private sealed class FakeStore : INoticeStore
    {
        public List<Notice> Added { get; } = [];

        public Exception? FailWith { get; set; }

        public Task AddAsync(Notice notice, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException(FailWith);
            }

            Added.Add(notice);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMailbox : IMailboxClient
    {
        public List<MailboxMessage> Inbox { get; } = [];

        public List<string> Moved { get; } = [];

        public Exception? FetchFailsWith { get; set; }

        public Exception? MoveFailsWith { get; set; }

        public Task<IReadOnlyList<MailboxMessage>> GetInboxMessagesAsync(
            int maxMessages, CancellationToken cancellationToken) =>
            FetchFailsWith is not null
                ? Task.FromException<IReadOnlyList<MailboxMessage>>(FetchFailsWith)
                : Task.FromResult<IReadOnlyList<MailboxMessage>>([.. Inbox.Take(maxMessages)]);

        public Task MoveToProcessedAsync(string messageId, CancellationToken cancellationToken)
        {
            if (MoveFailsWith is not null)
            {
                return Task.FromException(MoveFailsWith);
            }

            Moved.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private EmailIngestion Ingestion() =>
        new(_mailbox,
            _store,
            Options.Create(new MailboxOptions { Mailbox = "notices@example.test" }),
            NullLogger<EmailIngestion>.Instance);

    private static MailAttachment Pdf(string name, string body = "notice") =>
        new([.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(body)], name);

    private static MailboxMessage Message(
        string id = "AAMk-1",
        string? sender = "agent@bank.example",
        DateTime? sentAt = null,
        params MailAttachment[] attachments) =>
        new(id,
            sender,
            sentAt ?? new DateTime(2026, 3, 1, 8, 15, 0, DateTimeKind.Utc),
            attachments.Length > 0 ? attachments : [Pdf("notice.pdf")]);

    [Fact]
    public async Task AMessageIsRecordedThenMoved()
    {
        _mailbox.Inbox.Add(Message());

        await Ingestion().RunOnceAsync(default);

        var notice = Assert.Single(_store.Added);
        Assert.Equal(NoticeStatus.Received, notice.Status);
        Assert.Equal("AAMk-1", notice.EmailMessageId);
        Assert.Equal(["AAMk-1"], _mailbox.Moved);
    }

    /// <summary>
    /// The one ingestion path with real provenance. Sender and send time come from the
    /// envelope, not from when we happened to collect it.
    /// </summary>
    [Fact]
    public async Task SenderAndSentAtComeFromTheEnvelope()
    {
        var sentAt = new DateTime(2026, 2, 27, 16, 42, 0, DateTimeKind.Utc);
        _mailbox.Inbox.Add(Message(sender: "ops@agentbank.example", sentAt: sentAt));

        await Ingestion().RunOnceAsync(default);

        var notice = Assert.Single(_store.Added);
        Assert.Equal("ops@agentbank.example", notice.Sender);
        Assert.Equal(sentAt, notice.SentAtUtc);
        Assert.NotEqual(sentAt, notice.ReceivedAtUtc);
    }

    /// <summary>
    /// One mail carrying three notices is three documents to review, not one with the other
    /// two hidden inside it.
    /// </summary>
    [Fact]
    public async Task EachPdfAttachmentBecomesItsOwnNotice()
    {
        _mailbox.Inbox.Add(Message(
            attachments: [Pdf("reset.pdf", "one"), Pdf("payment.pdf", "two"), Pdf("fee.pdf", "three")]));

        await Ingestion().RunOnceAsync(default);

        Assert.Equal(3, _store.Added.Count);
        Assert.All(_store.Added, n => Assert.Equal("AAMk-1", n.EmailMessageId));
        Assert.Equal(3, _store.Added.Select(n => n.Sha256).Distinct().Count());
        Assert.Equal(["AAMk-1"], _mailbox.Moved);
    }

    /// <summary>
    /// The rule. Not recorded, so not moved — the message stays in the queue and the
    /// next poll picks it up, which is what makes a database outage lossless.
    /// </summary>
    [Fact]
    public async Task AMessageThatCannotBeRecordedIsNotMoved()
    {
        _mailbox.Inbox.Add(Message());
        _store.FailWith = new InvalidOperationException("database unavailable");

        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Empty(_mailbox.Moved);
    }

    [Fact]
    public async Task AMessageLeftUnreadIsIngestedOnceTheStoreRecovers()
    {
        _mailbox.Inbox.Add(Message());
        var ingestion = Ingestion();

        _store.FailWith = new InvalidOperationException("database unavailable");
        await ingestion.RunOnceAsync(default);
        Assert.Empty(_mailbox.Moved);

        _store.FailWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-1"], _mailbox.Moved);
    }

    /// <summary>
    /// A partial failure must not mark the message read. Doing so would lose the attachments
    /// that were never recorded, with nothing left to say they existed.
    /// </summary>
    [Fact]
    public async Task AMessageIsNotMovedWhenOnlySomeAttachmentsWereRecorded()
    {
        _mailbox.Inbox.Add(Message(attachments: [Pdf("first.pdf", "one"), Pdf("second.pdf", "two")]));

        var failing = new FailAfterFirst();
        var ingestion = new EmailIngestion(
            _mailbox,
            failing,
            Options.Create(new MailboxOptions { Mailbox = "notices@example.test" }),
            NullLogger<EmailIngestion>.Instance);

        await ingestion.RunOnceAsync(default);

        Assert.Single(failing.Added);
        Assert.Empty(_mailbox.Moved);
    }

    private sealed class FailAfterFirst : INoticeStore
    {
        public List<Notice> Added { get; } = [];

        public Task AddAsync(Notice notice, CancellationToken cancellationToken)
        {
            if (Added.Count > 0)
            {
                return Task.FromException(new InvalidOperationException("connection reset"));
            }

            Added.Add(notice);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Recorded but not moved: the message stays in the mailbox, but its notices are
    /// not recorded a second time within the run.
    /// </summary>
    /// <remarks>
    /// At-least-once still holds — a restart loses the in-memory record and ingests it once
    /// more, which is the same bound as any crash between committing and marking, and why
    /// EmailMessageId is not uniquely indexed. What is deliberately *not* allowed is
    /// unbounded growth while the process keeps running.
    /// </remarks>
    [Fact]
    public async Task AFailedMoveLeavesTheMessageInTheInboxWithoutRecordingItTwice()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.MoveFailsWith = new InvalidOperationException("throttled");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);
        Assert.Single(_store.Added);

        // Still in the inbox, so the next poll sees it again — and skips recording it.
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Empty(_mailbox.Moved);

        // A fresh process has no memory of it, so it is recorded once more. That is the
        // at-least-once bound, not a leak.
        var afterRestart = Ingestion();
        await afterRestart.RunOnceAsync(default);

        Assert.Equal(2, _store.Added.Count);
        Assert.Equal(_store.Added[0].Sha256, _store.Added[1].Sha256);
        Assert.Equal(_store.Added[0].EmailMessageId, _store.Added[1].EmailMessageId);
    }

    // --- the heartbeat, through ingestion ------------------------------------------------

    /// <summary>
    /// A fetch that throws loses nothing and stops the pass. The heartbeat is what turns it
    /// into a single, timely log entry.
    /// </summary>
    [Fact]
    public async Task AnUnreachableMailboxIsRecordedByTheHeartbeatAndLosesNothing()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.FetchFailsWith = new InvalidOperationException("host not found");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);

        Assert.Equal(ConnectionState.Down, ingestion.Heartbeat.State);
        Assert.Empty(_store.Added);
        Assert.Empty(_mailbox.Moved);
    }

    [Fact]
    public async Task TheHeartbeatComesBackUpWhenTheMailboxDoes()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.FetchFailsWith = new InvalidOperationException("host not found");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);
        Assert.Equal(ConnectionState.Down, ingestion.Heartbeat.State);

        _mailbox.FetchFailsWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.Equal(ConnectionState.Up, ingestion.Heartbeat.State);
        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-1"], _mailbox.Moved);
    }

    /// <summary>A poll that returns nothing still proves the mailbox is reachable.</summary>
    [Fact]
    public async Task AnEmptyMailboxCountsAsReachable()
    {
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);

        Assert.Equal(ConnectionState.Up, ingestion.Heartbeat.State);
        Assert.Empty(_store.Added);
    }

    // --- composition guards --------------------------------------------------------------

    [Fact]
    public void ADependencyThatIsNullIsRejectedAtConstruction()
    {
        var options = Options.Create(new MailboxOptions { Mailbox = "notices@example.test" });

        Assert.Throws<ArgumentNullException>(() =>
            new EmailIngestion(null!, _store, options, NullLogger<EmailIngestion>.Instance));

        Assert.Throws<ArgumentNullException>(() =>
            new EmailIngestion(_mailbox, null!, options, NullLogger<EmailIngestion>.Instance));

        Assert.Throws<ArgumentNullException>(() =>
            new EmailIngestion(_mailbox, _store, null!, NullLogger<EmailIngestion>.Instance));
    }

    /// <summary>
    /// No mailbox means every poll would ask Graph for the messages of an empty address and
    /// fail in a way that reads like a connectivity problem. Failing at construction makes it
    /// a composition error instead, thrown once, where the mistake actually is.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyMailboxAddressIsRejectedAtConstruction(string mailbox)
    {
        Assert.Throws<ArgumentException>(() =>
            new EmailIngestion(
                _mailbox,
                _store,
                Options.Create(new MailboxOptions { Mailbox = mailbox }),
                NullLogger<EmailIngestion>.Instance));
    }

    // --- attachments that could not be read ----------------------------------------------
    // The distinction between "carries no PDF" and "we failed to fetch its PDF" is the one
    // that decides whether a notice survives. Both look like an empty attachment list.

    /// <summary>
    /// A plain reply or a signature image: genuinely nothing to ingest, so it leaves the
    /// queue rather than being re-examined on every poll for ever.
    /// </summary>
    [Fact]
    public async Task AMessageWithNoPdfIsMovedWithoutRecordingAnything()
    {
        _mailbox.Inbox.Add(new MailboxMessage("AAMk-nopdf", "someone@example.test", null, []));

        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Equal(["AAMk-nopdf"], _mailbox.Moved);
    }

    /// <summary>
    /// The dangerous case. Its attachments could not be read, so it looks identical to the
    /// one above — but marking it read would file a notice we never got as handled, with
    /// nothing anywhere to say it existed.
    /// </summary>
    [Fact]
    public async Task AMessageWhoseAttachmentsCouldNotBeReadIsLeftInTheInbox()
    {
        _mailbox.Inbox.Add(new MailboxMessage(
            "AAMk-in the inboxable", "agent@bank.example", null, [], AttachmentsIncomplete: true));

        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Empty(_mailbox.Moved);
    }

    /// <summary>
    /// Partly read: what arrived is recorded, but the message stays in the queue so the rest
    /// is retried. The recorded ones arrive again — the accepted duplicate, not a lost notice.
    /// </summary>
    [Fact]
    public async Task AMessageWithSomeUnreadableAttachmentsRecordsWhatItHasAndStaysPut()
    {
        _mailbox.Inbox.Add(new MailboxMessage(
            "AAMk-partial",
            "agent@bank.example",
            null,
            [Pdf("got-this-one.pdf")],
            AttachmentsIncomplete: true));

        await Ingestion().RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Empty(_mailbox.Moved);
    }

    /// <summary>
    /// A failed move is not a connectivity failure — the fetch plainly succeeded — so it
    /// must not be reported as one, or a mailbox that is up gets logged as unreachable.
    /// </summary>
    [Fact]
    public async Task AFailedMoveDoesNotReportTheMailboxAsUnreachable()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.MoveFailsWith = new InvalidOperationException("throttled");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);

        Assert.Equal(ConnectionState.Up, ingestion.Heartbeat.State);
        Assert.Single(_store.Added);
    }

    /// <summary>
    /// And one bad message does not abandon the rest of the page.
    /// </summary>
    [Fact]
    public async Task AnUnreadableMessageDoesNotStopTheOnesBehindIt()
    {
        _mailbox.Inbox.Add(new MailboxMessage("AAMk-bad", null, null, [], AttachmentsIncomplete: true));
        _mailbox.Inbox.Add(Message(id: "AAMk-good"));

        await Ingestion().RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-good"], _mailbox.Moved);
    }

    /// <summary>
    /// The failure the live run actually hit: the app registration held Mail.Read, so every
    /// move was denied and the same notices were recorded on every poll. Not an outage —
    /// a silent flood. At a one-minute interval one stuck message is fourteen hundred
    /// duplicate notices a day, and the only sign is a warning nobody is reading.
    /// </summary>
    [Fact]
    public async Task APermanentMoveFailureDoesNotRecordTheSameNoticesOnEveryPoll()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.MoveFailsWith = new UnauthorizedAccessException("Access is denied.");
        var ingestion = Ingestion();

        for (var poll = 0; poll < 10; poll++)
        {
            await ingestion.RunOnceAsync(default);
        }

        // One record from the first poll, and nine attempts to mark it afterwards.
        Assert.Single(_store.Added);
        Assert.Empty(_mailbox.Moved);
    }

    /// <summary>
    /// And when whatever blocked the mark is fixed, the message leaves the queue without
    /// being recorded a second time.
    /// </summary>
    [Fact]
    public async Task AMessageRecordedButNotMovedIsMovedOnceTheBlockClears()
    {
        _mailbox.Inbox.Add(Message());
        _mailbox.MoveFailsWith = new UnauthorizedAccessException("Access is denied.");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);
        await ingestion.RunOnceAsync(default);
        Assert.Single(_store.Added);

        _mailbox.MoveFailsWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-1"], _mailbox.Moved);
    }
}
