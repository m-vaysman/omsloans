namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Reads the economic data out of a notice. The seam that keeps provider choice from leaking
/// into the rest of the system.
/// </summary>
/// <remarks>
/// <para>
/// Everything upstream and downstream talks to this; only the implementations know which
/// vendor they are calling — see
/// <see href="../../../docs/decisions/0001-cloud-llm-over-local.md">ADR 0001</see>. That is
/// also what makes reprocessing (#18) and the accuracy report (#19) possible at all: a
/// provider is a name resolved at run time rather than a code path.
/// </para>
/// <para>
/// No vendor SDK is referenced from this project, deliberately. The day one is added here is
/// the day the abstraction has stopped being one.
/// </para>
/// <para>
/// <strong>Implementations may throw <see cref="ExtractionProviderException"/></strong> to say
/// a call failed and whether it is worth trying again. Callers do not see that: what they hold
/// is the resilient decorator, which retries what is worth retrying and turns whatever is left
/// into a failed <see cref="ExtractionResult"/>. Only genuine cancellation propagates.
/// </para>
/// </remarks>
public interface INoticeExtractor
{
    /// <summary>
    /// The pinned model id, recorded on every row this extractor produces.
    /// </summary>
    /// <remarks>
    /// A pinned id rather than a family alias. "The latest one" makes an accuracy comparison
    /// meaningless: two rows would carry the same name and different behaviour, and nothing
    /// afterwards could tell them apart.
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
