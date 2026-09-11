namespace OmsLoan.Domain.Extractors;

/// <summary>
/// One attempt at reading a notice, successful or not.
/// </summary>
/// <remarks>
/// Always returned, never thrown. A down provider, an unparseable response, and a timeout are
/// outcomes to record, not exceptions to handle — an attempt with no row is one nobody can look at.
///
/// <see cref="RawJson"/> is the response verbatim whenever there was one, including parse failure.
/// That maps onto <see cref="OmsLoan.Domain.Extraction.RawJson"/>.
/// </remarks>
/// <param name="Outcome">How it ended.</param>
/// <param name="ModelName">Pinned model id for the stored row.</param>
/// <param name="PromptVersion">Prompt revision for the stored row.</param>
/// <param name="RawJson">Response exactly as returned. Empty when there was no response.</param>
/// <param name="Fields">
/// Parsed fields. Empty unless <see cref="Outcome"/> is <see cref="ExtractionOutcome.Succeeded"/>.
/// </param>
/// <param name="Telemetry">Cost and timing, recorded on failures too.</param>
/// <param name="Error">
/// Why it failed, one line, for the log and the operator. Null on success. Never a key, token, or notice fragment.
/// </param>
public sealed record ExtractionResult(
    ExtractionOutcome Outcome,
    string ModelName,
    string PromptVersion,
    string RawJson,
    IReadOnlyDictionary<string, string?> Fields,
    ExtractionTelemetry Telemetry,
    string? Error = null)
{
    public bool IsSuccess => Outcome == ExtractionOutcome.Succeeded;

    public static ExtractionResult Success(
        string modelName,
        string promptVersion,
        string rawJson,
        IReadOnlyDictionary<string, string?> fields,
        ExtractionTelemetry telemetry) =>
        new(ExtractionOutcome.Succeeded, modelName, promptVersion, rawJson, fields, telemetry);

    /// <summary>
    /// The provider answered and the answer could not be used. The response is kept.
    /// </summary>
    public static ExtractionResult ParseFailure(
        string modelName,
        string promptVersion,
        string rawJson,
        ExtractionTelemetry telemetry,
        string error) =>
        new(ExtractionOutcome.ParseFailed, modelName, promptVersion, rawJson, EmptyFields, telemetry, error);

    /// <summary>The provider did not answer usefully. Nothing to keep but the reason.</summary>
    public static ExtractionResult ProviderFailure(
        string modelName,
        string promptVersion,
        ExtractionTelemetry telemetry,
        string error) =>
        new(ExtractionOutcome.ProviderFailed, modelName, promptVersion, string.Empty, EmptyFields, telemetry, error);

    public static ExtractionResult Timeout(
        string modelName,
        string promptVersion,
        ExtractionTelemetry telemetry,
        string error) =>
        new(ExtractionOutcome.TimedOut, modelName, promptVersion, string.Empty, EmptyFields, telemetry, error);

    private static readonly IReadOnlyDictionary<string, string?> EmptyFields =
        new Dictionary<string, string?>();
}
