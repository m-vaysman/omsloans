using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;

namespace OmsLoan.Api.Controllers;

/// <summary>
/// Manual notice upload: the way a notice gets in when it did not arrive through a watched
/// folder or the shared mailbox.
/// </summary>
/// <remarks>
/// Validation happens here, first, and in order of cost. There is no point measuring,
/// reading, hashing or storing a file that was never going to be accepted.
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
    private const string PdfExtension = ".pdf";

    private readonly UploadOptions _upload = uploadOptions.Value;

    /// <summary>
    /// Records an uploaded PDF as a notice.
    /// </summary>
    /// <param name="file">The PDF. Must be named <c>*.pdf</c>.</param>
    /// <param name="sender">
    /// Who sent it, if the uploader knows — usually read off a forwarded email. Optional.
    /// </param>
    /// <param name="sentAtUtc">
    /// When the agent bank sent it, if known. Optional, and never inferred from the upload
    /// time: that is when it reached us, which is a different fact and is recorded separately.
    /// </param>
    /// <remarks>
    /// <para>
    /// Two cheap checks first, each one a reason to stop before anything is read:
    /// </para>
    /// <list type="number">
    /// <item>the filename ends in <c>.pdf</c> — costs nothing, needs no bytes read</item>
    /// <item>the size is within the limit — a number already on the request</item>
    /// </list>
    /// <para>
    /// Only then are the bytes read and checked for the <c>%PDF</c> marker, which is the check
    /// that actually decides — a filename is supplied by whoever sent the file, and the magic
    /// number is not.
    /// </para>
    /// <para>
    /// No duplicate check. Every upload is recorded, exactly as every file dropped in the
    /// watched folder is — deciding two arrivals are the same document is review's work, not
    /// ingestion's. That is why there is no 409 here.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        [FromForm] string? sender,
        [FromForm] DateTime? sentAtUtc,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem(
                title: "No file was uploaded.",
                detail: "Send the PDF as multipart/form-data under the field name 'file'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The extension, first and before anything else is looked at. It is free — no bytes
        // read, no length consulted — and we only ever accept PDFs, so a file that is not
        // named like one is rejected without measuring it or asking what it claims to be.
        // A missing extension is a rejection too: absence is not a pass.
        var extension = Path.GetExtension(file.FileName);

        if (!PdfExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            return UnsupportedPdf(
                file.FileName,
                string.IsNullOrEmpty(extension)
                    ? "the file has no extension, and only .pdf is accepted"
                    : $"the file extension was '{extension}', and only .pdf is accepted");
        }

        if (file.Length == 0)
        {
            return Problem(
                title: "The file is empty.",
                detail: "The upload contained no bytes.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // On the declared length, so an oversized upload is refused without being buffered.
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

        // No check on the declared content type. It was tried and removed: it rejected genuine
        // PDFs. A client that sends application/octet-stream — which is what fetch does for an
        // untyped Blob — or the legacy application/x-pdf was refused despite the filename and
        // the bytes both being right. It never caught anything the magic-byte check below does
        // not catch, so it was pure false-rejection risk.

        byte[] content;

        try
        {
            await using var stream = file.OpenReadStream();
            await using var buffer = new MemoryStream();

            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away mid-upload. Nothing was stored, there is nobody left to
            // answer, and this is not a server fault — let it end quietly rather than logging
            // an error somebody will investigate.
            throw;
        }
        catch (Exception ex)
        {
            // A truncated or aborted upload. The request is at fault, not the server, so 400
            // rather than 500 — but it is logged, because a rash of these is a network problem
            // rather than a user one.
            logger.LogWarning(ex, "Upload of {FileName} could not be read.", file.FileName);

            return Problem(
                title: "The upload did not complete.",
                detail: "The file could not be read in full. Try again.",
                statusCode: StatusCodes.Status400BadRequest);
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

        try
        {
            db.Notices.Add(notice);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The database is unreachable, timing out, or otherwise refusing the write. From
            // the uploader's side these are one situation — it did not save, try again later —
            // so they get one answer, and 503 is the one that says "later" rather than "never".
            //
            // Nothing partial is left behind: the notice is a single row in a single
            // SaveChanges, so a failure here stored nothing. The uploader still has the file.
            logger.LogError(
                ex,
                "Could not record uploaded {FileName} (sha256 {Hash}).",
                file.FileName,
                notice.Sha256);

            return Problem(
                title: "The notice could not be recorded.",
                detail: "The database is not available. Nothing was stored — try again shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

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
