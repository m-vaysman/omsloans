namespace OmsLoan.Domain.Extractors;

/// <summary>
/// One attempt at reading a notice, successful or not.
/// </summary>
/// <remarks>
/// <para>
/// Always returned, never thrown. A provider that is down, a response that will not parse and
/// a call that timed out are all outcomes to be recorded, not exceptions to be handled — an
/// attempt with no row is an attempt nobody can look at afterwards, and the failures are
/// exactly the ones worth looking at.
/// </para>
/// <para>
/// <see cref="RawJson"/> is the response verbatim and is filled in whenever there was one,
/// including when parsing it failed. That is what makes a bad extraction diagnosable months
/// later, and it maps straight onto <see cref="OmsLoan.Domain.Extraction.RawJson"/>.
/// </para>
/// </remarks>
/// <param name="Outcome">How it ended.</param>
/// <param name="ModelName">The pinned model id that produced this, for the stored row.</param>
/// <param name="PromptVersion">The prompt revision used, for the stored row.</param>
/// <param name="RawJson">The response exactly as returned. Empty when there was no response.</param>
/// <param name="Fields">
/// What was parsed out, keyed by field name. Empty unless <see cref="Outcome"/> is
/// <see cref="ExtractionOutcome.Succeeded"/>.
/// </param>
/// <param name="Telemetry">Cost and timing, recorded on failures too.</param>
/// <param name="Error">
/// Why it failed, in one line, for the log and the operator. Null on success. Never carries a
/// key, a token or a fragment of the notice.
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

    /// <summary>The provider did not answer usefully. There is nothing to keep but the reason.</summary>
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
