namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Reads economic data out of a notice. The seam that keeps provider choice from leaking upstream.
/// </summary>
/// <remarks>
/// Everything talks to this; only implementations know the vendor
/// (<see href="../../../docs/decisions/0001-cloud-llm-over-local.md">ADR 0001</see>).
/// No vendor SDK is referenced from Domain — the day one is added here, the abstraction has stopped.
///
/// Implementations may throw <see cref="ExtractionProviderException"/>. Callers do not see that:
/// they hold <see cref="GuardedNoticeExtractor"/>, which makes one attempt, applies the deadline,
/// and turns whatever is left into a failed <see cref="ExtractionResult"/>. Only genuine
/// cancellation propagates. There are no retries.
/// </remarks>
internal interface INoticeExtractor
{
    /// <summary>
    /// Pinned model id, recorded on every row this extractor produces.
    /// </summary>
    /// <remarks>
    /// Pinned, not a family alias. "The latest one" makes accuracy comparisons meaningless:
    /// two rows would share a name and different behaviour.
    /// </remarks>
    string ModelName { get; }

    /// <summary>
    /// Sends the notice to the provider and reads the response.
    /// </summary>
    /// <param name="pdfBytes">The notice exactly as received. Never a text rendering of it.</param>
    /// <param name="noticeType">Which prompt and schema to use.</param>
    /// <param name="cancellationToken">Shutdown. Not the request timeout, which is configured per provider.</param>
    Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken);
}
