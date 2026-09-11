namespace OmsLoan.Api;

/// <summary>
/// Limits on manual notice upload, read from the <c>Upload</c> configuration section.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>
    /// Largest accepted upload. Above it: 413.
    /// </summary>
    /// <remarks>
    /// Notices vary: a rate reset is a page; a credit-agreement amendment can be a hundred.
    /// Default is generous — refusing a real notice beats storing a few unused megabytes.
    ///
    /// Request body is capped at this plus a multipart allowance so a wildly oversized upload
    /// is rejected before buffering.
    /// </remarks>
    public long MaxBytes { get; set; } = 32 * 1024 * 1024;
}
