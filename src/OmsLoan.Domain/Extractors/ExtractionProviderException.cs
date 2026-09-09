namespace OmsLoan.Domain.Extractors;

/// <summary>
/// A provider call failed. Carries whether trying again is worth anything.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is the whole reason this type exists. A 429 or a 503 is weather: wait and
/// it passes. A 401 or a 400 is a fact about the request, and retrying it three times with
/// backoff turns a five-second failure into a thirty-second one and calls the provider three
/// times to be told the same thing.
/// </para>
/// <para>
/// Providers decide, because only they know their own vocabulary; the decorator acts on the
/// answer. <see cref="FromStatusCode"/> covers the ordinary HTTP shape so each provider does
/// not reinvent it.
/// </para>
/// </remarks>
public sealed class ExtractionProviderException : Exception
{
    public ExtractionProviderException(string message, bool isTransient, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        IsTransient = isTransient;
        StatusCode = statusCode;
    }

    /// <summary>Whether the same call might succeed later.</summary>
    public bool IsTransient { get; }

    /// <summary>The provider's HTTP status, when there was one.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The usual reading of an HTTP status: 429 and 5xx are worth retrying, other 4xx are not.
    /// </summary>
    /// <remarks>
    /// 408 is the exception among the 4xx — a request timeout is the server saying it gave up
    /// waiting, not that the request was wrong.
    /// </remarks>
    public static ExtractionProviderException FromStatusCode(
        int statusCode,
        string message,
        Exception? inner = null) =>
        new(message, IsTransientStatus(statusCode), statusCode, inner);

    public static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 or >= 500 and <= 599;
}
