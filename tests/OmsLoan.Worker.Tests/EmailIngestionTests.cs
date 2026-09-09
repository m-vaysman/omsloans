using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// Mailbox ingestion, and above all the ordering rule: a message is never marked read until
/// its notices are committed.
/// </summary>
/// <remarks>
/// Both the mailbox and the store are fakes that fail on demand. "The message stays unread
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
        public List<MailboxMessage> Unread { get; } = [];

        public List<string> MarkedRead { get; } = [];

        public Exception? FetchFailsWith { get; set; }

        public Exception? MarkFailsWith { get; set; }

        public Task<IReadOnlyList<MailboxMessage>> GetUnreadWithPdfAttachmentsAsync(
            int maxMessages, CancellationToken cancellationToken) =>
            FetchFailsWith is not null
                ? Task.FromException<IReadOnlyList<MailboxMessage>>(FetchFailsWith)
                : Task.FromResult<IReadOnlyList<MailboxMessage>>([.. Unread.Take(maxMessages)]);

        public Task MarkReadAsync(string messageId, CancellationToken cancellationToken)
        {
            if (MarkFailsWith is not null)
            {
                return Task.FromException(MarkFailsWith);
            }

            MarkedRead.Add(messageId);
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
    public async Task AMessageIsRecordedThenMarkedRead()
    {
        _mailbox.Unread.Add(Message());

        await Ingestion().RunOnceAsync(default);

        var notice = Assert.Single(_store.Added);
        Assert.Equal(NoticeStatus.Received, notice.Status);
        Assert.Equal("AAMk-1", notice.EmailMessageId);
        Assert.Equal(["AAMk-1"], _mailbox.MarkedRead);
    }

    /// <summary>
    /// The one ingestion path with real provenance. Sender and send time come from the
    /// envelope, not from when we happened to collect it.
    /// </summary>
    [Fact]
    public async Task SenderAndSentAtComeFromTheEnvelope()
    {
        var sentAt = new DateTime(2026, 2, 27, 16, 42, 0, DateTimeKind.Utc);
        _mailbox.Unread.Add(Message(sender: "ops@agentbank.example", sentAt: sentAt));

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
        _mailbox.Unread.Add(Message(
            attachments: [Pdf("reset.pdf", "one"), Pdf("payment.pdf", "two"), Pdf("fee.pdf", "three")]));

        await Ingestion().RunOnceAsync(default);

        Assert.Equal(3, _store.Added.Count);
        Assert.All(_store.Added, n => Assert.Equal("AAMk-1", n.EmailMessageId));
        Assert.Equal(3, _store.Added.Select(n => n.Sha256).Distinct().Count());
        Assert.Equal(["AAMk-1"], _mailbox.MarkedRead);
    }

    /// <summary>
    /// The rule. Not recorded, so not marked read — the message stays in the queue and the
    /// next poll picks it up, which is what makes a database outage lossless.
    /// </summary>
    [Fact]
    public async Task AMessageThatCannotBeRecordedIsNotMarkedRead()
    {
        _mailbox.Unread.Add(Message());
        _store.FailWith = new InvalidOperationException("database unavailable");

        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Empty(_mailbox.MarkedRead);
    }

    [Fact]
    public async Task AMessageLeftUnreadIsIngestedOnceTheStoreRecovers()
    {
        _mailbox.Unread.Add(Message());
        var ingestion = Ingestion();

        _store.FailWith = new InvalidOperationException("database unavailable");
        await ingestion.RunOnceAsync(default);
        Assert.Empty(_mailbox.MarkedRead);

        _store.FailWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-1"], _mailbox.MarkedRead);
    }

    /// <summary>
    /// A partial failure must not mark the message read. Doing so would lose the attachments
    /// that were never recorded, with nothing left to say they existed.
    /// </summary>
    [Fact]
    public async Task AMessageIsNotMarkedReadWhenOnlySomeAttachmentsWereRecorded()
    {
        _mailbox.Unread.Add(Message(attachments: [Pdf("first.pdf", "one"), Pdf("second.pdf", "two")]));

        var failing = new FailAfterFirst();
        var ingestion = new EmailIngestion(
            _mailbox,
            failing,
            Options.Create(new MailboxOptions { Mailbox = "notices@example.test" }),
            NullLogger<EmailIngestion>.Instance);

        await ingestion.RunOnceAsync(default);

        Assert.Single(failing.Added);
        Assert.Empty(_mailbox.MarkedRead);
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
    /// Recorded but not marked read: the message is read again next poll and recorded again.
    /// At-least-once, and why EmailMessageId is no longer uniquely indexed.
    /// </summary>
    [Fact]
    public async Task AFailedMarkReadLeavesTheMessageToBeIngestedAgain()
    {
        _mailbox.Unread.Add(Message());
        _mailbox.MarkFailsWith = new InvalidOperationException("throttled");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);
        Assert.Single(_store.Added);

        // Still unread, so the next poll sees it again.
        await ingestion.RunOnceAsync(default);

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
        _mailbox.Unread.Add(Message());
        _mailbox.FetchFailsWith = new InvalidOperationException("host not found");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);

        Assert.False(ingestion.Heartbeat.IsUp);
        Assert.Empty(_store.Added);
        Assert.Empty(_mailbox.MarkedRead);
    }

    [Fact]
    public async Task TheHeartbeatComesBackUpWhenTheMailboxDoes()
    {
        _mailbox.Unread.Add(Message());
        _mailbox.FetchFailsWith = new InvalidOperationException("host not found");
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);
        Assert.False(ingestion.Heartbeat.IsUp);

        _mailbox.FetchFailsWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.True(ingestion.Heartbeat.IsUp);
        Assert.Single(_store.Added);
        Assert.Equal(["AAMk-1"], _mailbox.MarkedRead);
    }

    /// <summary>A poll that returns nothing still proves the mailbox is reachable.</summary>
    [Fact]
    public async Task AnEmptyMailboxCountsAsReachable()
    {
        var ingestion = Ingestion();

        await ingestion.RunOnceAsync(default);

        Assert.True(ingestion.Heartbeat.IsUp);
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
}
