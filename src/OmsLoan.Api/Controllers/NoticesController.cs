using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;

namespace OmsLoan.Api.Controllers;

/// <summary>
/// Manual notice upload: the way a notice gets in when it did not arrive through a watched
/// folder or the shared mailbox.
/// </summary>
/// <remarks>
/// Validation happens here, first, and cheaply. There is no point hashing a file, opening a
/// transaction and touching the database to discover the upload was a spreadsheet.
///
/// The notice itself is built by <see cref="NoticeContent"/>, the same code folder ingestion
/// uses, so identical bytes produce an identical row whichever way they arrived. A second
/// implementation here is how the hash of an uploaded notice and a folder-ingested one
/// quietly stop matching.
/// </remarks>
[ApiController]
[Route("api/notices")]
public class NoticesController(
    OmsLoanDbContext db,
    IOptions<UploadOptions> uploadOptions,
    ILogger<NoticesController> logger) : ControllerBase
{
    private readonly UploadOptions _upload = uploadOptions.Value;

    /// <summary>
    /// Records an uploaded PDF as a notice.
    /// </summary>
    /// <param name="file">The PDF.</param>
    /// <param name="sender">
    /// Who sent it, if the uploader knows — usually read off a forwarded email. Optional.
    /// </param>
    /// <param name="sentAtUtc">
    /// When the agent bank sent it, if known. Optional, and never inferred from the upload
    /// time: that is when it reached us, which is a different fact and is recorded separately.
    /// </param>
    /// <remarks>
    /// No duplicate check. Every upload is recorded, exactly as every file dropped in the
    /// watched folder is — deciding two arrivals are the same document is review's work, not
    /// ingestion's. That is why there is no 409 here.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        [FromForm] string? sender,
        [FromForm] DateTime? sentAtUtc,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                title: "No file was uploaded.",
                detail: "Send the PDF as multipart/form-data under the field name 'file'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Checked before reading, so an oversized upload is refused on its declared length
        // rather than after buffering it.
        if (file.Length > _upload.MaxBytes)
        {
            logger.LogInformation(
                "Rejected upload {FileName}: {Length} bytes exceeds the {Max} byte limit.",
                file.FileName,
                file.Length,
                _upload.MaxBytes);

            return Problem(
                title: "The file is too large.",
                detail: $"The maximum accepted size is {_upload.MaxBytes} bytes; this upload is {file.Length}.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // The declared content type, which is the cheap check and the one a browser gets right
        // most of the time. It is not trusted on its own — the bytes are checked below — but
        // it costs nothing and rejects the obvious cases before anything is read.
        if (!string.IsNullOrEmpty(file.ContentType)
            && !file.ContentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return UnsupportedPdf(file.FileName, $"the declared content type was '{file.ContentType}'");
        }

        byte[] content;

        await using (var stream = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        // The bytes themselves, which is the check that actually decides. A content type and a
        // file extension are both supplied by whoever sent the file; the magic number is not.
        if (!NoticeContent.IsPdf(content))
        {
            return UnsupportedPdf(file.FileName, "the file does not begin with the PDF marker");
        }

        var notice = NoticeContent.Create(
            content,
            receivedAtUtc: DateTime.UtcNow,
            sender: sender,
            sentAtUtc: sentAtUtc);

        db.Notices.Add(notice);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Uploaded {FileName} (sha256 {Hash}) recorded as notice {NoticeId}.",
            file.FileName,
            notice.Sha256,
            notice.NoticeId);

        return CreatedAtAction(nameof(Upload), new { id = notice.NoticeId }, new
        {
            noticeId = notice.NoticeId,
            sha256 = notice.Sha256,
            receivedAtUtc = notice.ReceivedAtUtc,
        });
    }

    /// <summary>
    /// 415 rather than 400: the request is well formed, we simply do not accept this kind of
    /// document. The reason is spelled out because "unsupported media type" on its own leaves
    /// an uploader guessing whether they picked the wrong file or hit a bug.
    /// </summary>
    private ObjectResult UnsupportedPdf(string fileName, string reason)
    {
        logger.LogInformation("Rejected upload {FileName}: {Reason}.", fileName, reason);

        return Problem(
            title: "Only PDF files are accepted.",
            detail: $"Rejected because {reason}.",
            statusCode: StatusCodes.Status415UnsupportedMediaType);
    }
}
