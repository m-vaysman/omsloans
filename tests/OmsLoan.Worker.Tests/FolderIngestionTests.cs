using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;
using OmsLoan.Worker.Ingestion;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// Folder ingestion, and above all the ordering rule: a file is never moved until its row is
/// committed.
/// </summary>
/// <remarks>
/// The store is a fake precisely so it can fail on demand. "The file stays put when the
/// database is unavailable" is the behaviour the whole design rests on, and a real database
/// makes it awkward to arrange.
/// </remarks>
public class FolderIngestionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "omsloan-ingest-" + Guid.NewGuid().ToString("N"));

    private readonly FakeStore _store = new();

    public FolderIngestionTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var name in WatchedFolder.Subfolders)
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

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

    private FolderIngestion Ingestion(int maxReadAttempts = 10) =>
        new(_store,
            Options.Create(new IngestionOptions
            {
                WatchedFolder = _root,
                MaxReadAttempts = maxReadAttempts,
            }),
            NullLogger<FolderIngestion>.Instance);

    private string DropPdf(string name, string body = "hello")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(body)]);
        return path;
    }

    private string[] FilesIn(string subfolder) =>
        Directory.GetFiles(Path.Combine(_root, subfolder)).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task AFileIsRecordedThenMovedToProcessed()
    {
        var file = DropPdf("notice.pdf");

        await Ingestion().RunOnceAsync(default);

        var notice = Assert.Single(_store.Added);
        Assert.Equal(NoticeStatus.Received, notice.Status);
        Assert.Null(notice.SentAtUtc);
        Assert.Null(notice.Sender);
        Assert.NotEqual(default, notice.ReceivedAtUtc);
        Assert.Equal(FolderIngestion.Sha256Hex(File.ReadAllBytes(Path.Combine(_root, "processed", "notice.pdf"))), notice.Sha256);

        Assert.False(File.Exists(file));
        Assert.Equal(["notice.pdf"], FilesIn("processed"));
    }

    /// <summary>
    /// The rule, stated as a test. If the row is not committed the file must not move, so
    /// that the next poll finds it and nothing is lost while the database is down.
    /// </summary>
    [Fact]
    public async Task AFileThatCannotBeRecordedStaysExactlyWhereItIs()
    {
        var file = DropPdf("notice.pdf");
        _store.FailWith = new InvalidOperationException("database unavailable");

        await Ingestion().RunOnceAsync(default);

        Assert.True(File.Exists(file));
        Assert.Empty(FilesIn("processed"));
        Assert.Empty(FilesIn("failed"));
    }

    /// <summary>
    /// And it is picked up when the database comes back — no manual replay.
    /// </summary>
    [Fact]
    public async Task AFileLeftBehindIsIngestedOnceTheStoreRecovers()
    {
        DropPdf("notice.pdf");
        var ingestion = Ingestion();

        _store.FailWith = new InvalidOperationException("database unavailable");
        await ingestion.RunOnceAsync(default);
        Assert.Empty(_store.Added);

        _store.FailWith = null;
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["notice.pdf"], FilesIn("processed"));
    }

    /// <summary>
    /// Repeated database failure must never move the file aside. The watched folder is the
    /// queue during an outage, and the read-attempt limit deliberately does not apply here —
    /// otherwise a long outage would quietly file good notices under failed/.
    /// </summary>
    [Fact]
    public async Task RepeatedStoreFailuresNeverMoveTheFileToFailed()
    {
        DropPdf("notice.pdf");
        _store.FailWith = new InvalidOperationException("database unavailable");
        var ingestion = Ingestion(maxReadAttempts: 2);

        for (var i = 0; i < 5; i++)
        {
            await ingestion.RunOnceAsync(default);
        }

        Assert.True(File.Exists(Path.Combine(_root, "notice.pdf")));
        Assert.Empty(FilesIn("failed"));
    }

    /// <summary>
    /// A file still being written is not read half-copied and stored as a truncated notice.
    /// </summary>
    [Fact]
    public async Task AFileStillBeingWrittenIsSkippedAndRetriedLater()
    {
        var path = Path.Combine(_root, "notice.pdf");
        var ingestion = Ingestion();

        using (var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            held.Write("%PDF-1.7"u8);
            held.Flush();

            await ingestion.RunOnceAsync(default);

            Assert.Empty(_store.Added);
            Assert.True(File.Exists(path));
        }

        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["notice.pdf"], FilesIn("processed"));
    }

    /// <summary>
    /// The escape hatch. Without it the "nothing moves until it is recorded" rule has no
    /// exit and one unreadable file is retried and logged on every poll for ever.
    /// </summary>
    [Fact]
    public async Task AFileThatCanNeverBeReadIsMovedToFailedAfterTheAttemptLimit()
    {
        var path = Path.Combine(_root, "stuck.pdf");
        var ingestion = Ingestion(maxReadAttempts: 3);

        // FileShare.Delete, not None: the reader still cannot open it — it asks for exclusive
        // access and this handle is in the way — but the file can still be renamed out of the
        // folder. That is the case worth testing, a file that is permanently unreadable yet
        // movable. Holding it with FileShare.None would block the move as well and prove only
        // that a locked file stays put, which the previous test already covers.
        using var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Delete);
        held.Write("%PDF-1.7"u8);
        held.Flush();

        await ingestion.RunOnceAsync(default);
        await ingestion.RunOnceAsync(default);

        Assert.True(File.Exists(path));
        Assert.Empty(FilesIn("failed"));

        // The third failure reaches the limit.
        await ingestion.RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Equal(["stuck.pdf"], FilesIn("failed"));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// The opposite case, and the more common one: a copy slow enough to exhaust the attempt
    /// limit still gets ingested once it finishes, rather than being filed under failed.
    /// </summary>
    [Fact]
    public async Task AFileThatBecomesReadableIsIngestedRatherThanFailed()
    {
        var path = Path.Combine(_root, "slow.pdf");
        var ingestion = Ingestion(maxReadAttempts: 2);

        using (var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            held.Write("%PDF-1.7"u8);
            held.Flush();

            await ingestion.RunOnceAsync(default);
            await ingestion.RunOnceAsync(default);
            await ingestion.RunOnceAsync(default);
        }

        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
        Assert.Equal(["slow.pdf"], FilesIn("processed"));
        Assert.Empty(FilesIn("failed"));
    }

    [Fact]
    public async Task SomethingThatIsNotAPdfGoesStraightToFailedWithoutBeingRecorded()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not a pdf");

        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
        Assert.Equal(["notes.txt"], FilesIn("failed"));
    }

    /// <summary>
    /// No deduplication. The same bytes arriving twice produce two notices, because deciding
    /// they are the same document is review's job.
    /// </summary>
    [Fact]
    public async Task TheSameDocumentTwiceProducesTwoNotices()
    {
        var ingestion = Ingestion();

        DropPdf("first.pdf", "identical");
        await ingestion.RunOnceAsync(default);

        DropPdf("second.pdf", "identical");
        await ingestion.RunOnceAsync(default);

        Assert.Equal(2, _store.Added.Count);
        Assert.Equal(_store.Added[0].Sha256, _store.Added[1].Sha256);
    }

    /// <summary>
    /// Agent banks reuse filenames, so an archived file is never overwritten — that would
    /// destroy the evidence for a notice already recorded.
    /// </summary>
    [Fact]
    public async Task AnArchivedFileIsNotOverwrittenByALaterOneOfTheSameName()
    {
        var ingestion = Ingestion();

        DropPdf("statement.pdf", "first");
        await ingestion.RunOnceAsync(default);

        DropPdf("statement.pdf", "second");
        await ingestion.RunOnceAsync(default);

        Assert.Equal(2, FilesIn("processed").Length);
    }

    /// <summary>
    /// Archived files must not be re-ingested on the next pass, or every poll would re-record
    /// everything ever received.
    /// </summary>
    [Fact]
    public async Task ArchivedFilesAreNotPickedUpAgain()
    {
        var ingestion = Ingestion();

        DropPdf("notice.pdf");
        await ingestion.RunOnceAsync(default);
        await ingestion.RunOnceAsync(default);
        await ingestion.RunOnceAsync(default);

        Assert.Single(_store.Added);
    }

    [Fact]
    public async Task AnEmptyFolderIsFine()
    {
        await Ingestion().RunOnceAsync(default);

        Assert.Empty(_store.Added);
    }
}
