namespace OmsLoan.Domain.Extractors;

/// <summary>
/// A provider call failed.
/// </summary>
/// <remarks>
/// Carries the HTTP status when there was one, so the recorded failure says <c>HTTP 429</c>
/// rather than a bare sentence — rate limit versus bad key is what a reviewer deciding to rerun needs.
///
/// It does not say whether the call is worth repeating: nothing here repeats anything. A provider
/// that does not answer has answered; the extraction is recorded failed and a person decides.
///
/// Throwing this is optional. The guard turns any exception into a recorded failure; this exists
/// only for a tidier message.
/// </remarks>
public sealed class ExtractionProviderException(
    string message,
    int? statusCode = null,
    Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The provider's HTTP status, when there was one.</summary>
    public int? StatusCode { get; } = statusCode;
}
