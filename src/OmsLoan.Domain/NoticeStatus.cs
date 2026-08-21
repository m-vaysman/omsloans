namespace OmsLoan.Domain;

/// <summary>
/// Lifecycle of a notice from ingestion through review.
/// </summary>
/// <remarks>
/// Persisted as a string, so adding a member here does not require a migration
/// or a data fix-up of existing rows.
/// </remarks>
public enum NoticeStatus
{
    /// <summary>Ingested and stored, not yet sent to a provider.</summary>
    Received,

    /// <summary>A current extraction exists and is waiting for a reviewer.</summary>
    Extracted,

    /// <summary>Every extraction attempt failed; needs operator attention.</summary>
    ExtractionFailed,

    /// <summary>A reviewer accepted the extracted values.</summary>
    Approved,

    /// <summary>A reviewer rejected the notice as unusable or out of scope.</summary>
    Rejected,
}
