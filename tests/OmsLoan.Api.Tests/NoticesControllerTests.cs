using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmsLoan.Api;
using OmsLoan.Api.Controllers;
using OmsLoan.Domain;

namespace OmsLoan.Api.Tests;

/// <summary>
/// The upload endpoint. Mostly about what it refuses, and how early.
/// </summary>
public class NoticesControllerTests : IDisposable
{
    private readonly OmsLoanDbContext _db = new(
        new DbContextOptionsBuilder<OmsLoanDbContext>()
            .UseInMemoryDatabase("notices-" + Guid.NewGuid().ToString("N"))
            .Options);

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private NoticesController Controller(long maxBytes = 32 * 1024 * 1024, OmsLoanDbContext? db = null) =>
        new(db ?? _db,
            Options.Create(new UploadOptions { MaxBytes = maxBytes }),
            NullLogger<NoticesController>.Instance);

    /// <summary>A context whose save always fails, standing in for a database that is down.</summary>
    private sealed class UnreachableDatabase(DbContextOptions<OmsLoanDbContext> options)
        : OmsLoanDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A network-related error occurred while establishing a connection.");
    }

    private static IFormFile File(byte[] content, string name = "notice.pdf", string contentType = "application/pdf") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static byte[] Pdf(string body = "hello") =>
        [.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(body)];

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCode => statusCode.StatusCode,
        _ => 0,
    };

    [Fact]
    public async Task AValidPdfIsRecordedAndReturnsCreated()
    {
        var result = await Controller().Upload(File(Pdf()), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));

        var notice = Assert.Single(_db.Notices);
        Assert.Equal(NoticeStatus.Received, notice.Status);
        Assert.Equal(NoticeContent.Sha256Hex(Pdf()), notice.Sha256);
        Assert.NotEqual(default, notice.ReceivedAtUtc);
    }

    /// <summary>
    /// The hash must match what folder ingestion would have produced for the same bytes —
    /// that is the whole point of both going through <see cref="NoticeContent"/>.
    /// </summary>
    [Fact]
    public async Task TheHashMatchesWhatFolderIngestionWouldProduce()
    {
        var bytes = Pdf("identical");

        await Controller().Upload(File(bytes), sender: null, sentAtUtc: null, default);

        Assert.Equal(NoticeContent.Sha256Hex(bytes), _db.Notices.Single().Sha256);
    }

    [Fact]
    public async Task SenderAndSentAtAreRecordedWhenSupplied()
    {
        var sentAt = new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc);

        await Controller().Upload(File(Pdf()), sender: " agent@bank.example ", sentAtUtc: sentAt, default);

        var notice = _db.Notices.Single();
        Assert.Equal("agent@bank.example", notice.Sender);
        Assert.Equal(sentAt, notice.SentAtUtc);
    }

    /// <summary>
    /// Absent means unknown, and unknown is recorded as null rather than as the upload time —
    /// when it reached us is a different fact and is already on ReceivedAtUtc.
    /// </summary>
    [Fact]
    public async Task AbsentSenderAndSentAtStayNull()
    {
        await Controller().Upload(File(Pdf()), sender: "   ", sentAtUtc: null, default);

        var notice = _db.Notices.Single();
        Assert.Null(notice.Sender);
        Assert.Null(notice.SentAtUtc);
    }

    [Fact]
    public async Task AMissingFileIsRefused()
    {
        var result = await Controller().Upload(file: null, sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    [Fact]
    public async Task AnEmptyFileIsRefused()
    {
        var result = await Controller().Upload(File([]), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    /// <summary>
    /// The declared content type is ignored entirely. Checking it rejected genuine PDFs:
    /// application/octet-stream is what fetch sends for an untyped Blob, and application/x-pdf
    /// is a real legacy variant. It never caught anything the magic-byte check does not, so it
    /// was pure false-rejection risk.
    /// </summary>
    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/x-pdf")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("")]
    public async Task TheDeclaredContentTypeIsIgnored(string contentType)
    {
        var result = await Controller().Upload(
            File(Pdf(), "notice.pdf", contentType), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        Assert.Single(_db.Notices);
    }

    /// <summary>
    /// And dropping it loses nothing: the bytes still decide, whatever the header claimed.
    /// </summary>
    [Fact]
    public async Task ANonPdfIsStillRefusedNoMatterWhatItDeclares()
    {
        var result = await Controller().Upload(
            File(System.Text.Encoding.UTF8.GetBytes("PK this is a zip"), "notice.pdf", "application/pdf"),
            sender: null,
            sentAtUtc: null,
            default);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    /// <summary>
    /// The check that matters. A content type and a file extension are both supplied by
    /// whoever sent the file; the magic number is not.
    /// </summary>
    [Fact]
    public async Task SomethingNamedPdfAndDeclaredPdfButNotPdfIsRefused()
    {
        var result = await Controller().Upload(
            File(System.Text.Encoding.UTF8.GetBytes("PK this is a zip")),
            sender: null,
            sentAtUtc: null,
            default);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    [Fact]
    public async Task AnOversizeUploadIsRefusedWithPayloadTooLarge()
    {
        var result = await Controller(maxBytes: 8).Upload(File(Pdf("well over eight bytes")), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    /// <summary>
    /// Size is judged before the file is read, so an oversized upload never gets buffered.
    /// A stream that throws on read proves the check happened first.
    /// </summary>
    [Fact]
    public async Task AnOversizeUploadIsRefusedWithoutReadingIt()
    {
        var unreadable = new FormFile(new ThrowingStream(), 0, 5_000_000, "file", "huge.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        var result = await Controller(maxBytes: 1024).Upload(unreadable, sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("the file must not be read");
    }

    /// <summary>
    /// No duplicate check: the same document uploaded twice produces two notices. Deciding
    /// they are the same is review's work, which is why there is no 409 here.
    /// </summary>
    [Fact]
    public async Task TheSameDocumentTwiceProducesTwoNoticesAndNoConflict()
    {
        var controller = Controller();
        var bytes = Pdf("identical");

        var first = await controller.Upload(File(bytes), sender: null, sentAtUtc: null, default);
        var second = await controller.Upload(File(bytes), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(first));
        Assert.Equal(StatusCodes.Status201Created, StatusOf(second));

        var notices = _db.Notices.ToList();
        Assert.Equal(2, notices.Count);
        Assert.Equal(notices[0].Sha256, notices[1].Sha256);
    }

    // --- the extension gate ------------------------------------------------------------
    // Cheapest check, so it runs first: no bytes read, no length consulted.

    [Theory]
    [InlineData("notice.txt")]
    [InlineData("notice.pdf.exe")]
    [InlineData("notice")]
    [InlineData("")]
    public async Task AFileNotNamedPdfIsRefusedEvenWhenEverythingElseIsRight(string fileName)
    {
        var result = await Controller().Upload(
            File(Pdf(), fileName), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
        Assert.Empty(_db.Notices);
    }

    [Theory]
    [InlineData("notice.PDF")]
    [InlineData("notice.Pdf")]
    public async Task TheExtensionCheckIsCaseInsensitive(string fileName)
    {
        var result = await Controller().Upload(
            File(Pdf(), fileName), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    /// <summary>
    /// Ordering, stated as a test: there is no point measuring a file that was never going to
    /// be accepted. An oversized upload with the wrong extension is refused on the extension.
    /// </summary>
    [Fact]
    public async Task TheExtensionIsCheckedBeforeTheSizeLimit()
    {
        var result = await Controller(maxBytes: 8).Upload(
            File(Pdf("well over eight bytes"), "notice.txt"), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
    }

    /// <summary>The extension is checked without reading a byte.</summary>
    [Fact]
    public async Task AWrongExtensionIsRefusedWithoutReadingTheFile()
    {
        var unreadable = new FormFile(new ThrowingStream(), 0, 100, "file", "notice.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        var result = await Controller().Upload(unreadable, sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
    }

    // --- failures below the validation gate --------------------------------------------

    /// <summary>
    /// The database being down is not the uploader's fault and is not permanent, so 503 and a
    /// "try again" rather than a 500 and a stack trace. Nothing partial is stored: the notice
    /// is one row in one SaveChanges.
    /// </summary>
    [Fact]
    public async Task ADatabaseFailureReturnsServiceUnavailableRatherThanThrowing()
    {
        using var unreachable = new UnreachableDatabase(
            new DbContextOptionsBuilder<OmsLoanDbContext>()
                .UseInMemoryDatabase("unreachable-" + Guid.NewGuid().ToString("N"))
                .Options);

        var result = await Controller(db: unreachable).Upload(
            File(Pdf()), sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusOf(result));
    }

    /// <summary>
    /// A truncated or aborted upload is the request's fault, not the server's, so 400 — but it
    /// must not escape as an unhandled exception.
    /// </summary>
    [Fact]
    public async Task AnUnreadableUploadIsRefusedRatherThanThrowing()
    {
        var unreadable = new FormFile(new ThrowingStream(), 0, 100, "file", "notice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        var result = await Controller().Upload(unreadable, sender: null, sentAtUtc: null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(_db.Notices);
    }
}
