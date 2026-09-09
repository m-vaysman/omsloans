namespace OmsLoan.Domain.Extractors;

/// <summary>
/// A provider call failed.
/// </summary>
/// <remarks>
/// <para>
/// Carries the HTTP status when there was one, so the recorded failure says <c>HTTP 429</c>
/// rather than a bare sentence — a reviewer deciding whether to run it again wants to know
/// whether the provider was rate-limiting us or rejecting the key.
/// </para>
/// <para>
/// It does <em>not</em> say whether the call is worth repeating, because nothing here repeats
/// anything. A provider that does not answer has answered; the extraction is recorded as
/// failed and a person decides what to do about it.
/// </para>
/// <para>
/// Throwing this is optional. The guard turns any exception into a recorded failure, so a
/// provider is free to let its own SDK's exception escape — this exists only to produce a
/// tidier message.
/// </para>
/// </remarks>
public sealed class ExtractionProviderException(
    string message,
    int? statusCode = null,
    Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The provider's HTTP status, when there was one.</summary>
    public int? StatusCode { get; } = statusCode;
}
