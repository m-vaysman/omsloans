namespace OmsLoan.Api;

/// <summary>
/// Limits on manual notice upload, read from the <c>Upload</c> configuration section.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>
    /// Largest accepted upload. Anything above it is refused with 413.
    /// </summary>
    /// <remarks>
    /// Configurable because notices vary: a rate reset is a page, a credit agreement
    /// amendment can be a hundred. The default is deliberately generous — the cost of a
    /// too-small limit is a reviewer unable to file a real notice, which is worse than
    /// storing a few megabytes we did not need.
    ///
    /// The request body is capped at this value plus a small allowance for the multipart
    /// envelope, so a wildly oversized upload is rejected by the server before it is buffered
    /// rather than after.
    /// </remarks>
    public long MaxBytes { get; set; } = 32 * 1024 * 1024;
}
