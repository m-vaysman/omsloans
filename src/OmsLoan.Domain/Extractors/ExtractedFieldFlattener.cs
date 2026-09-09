using System.Globalization;
using JsonFlatten;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Turns the model's nested response into the flat name/value rows
/// <see cref="ExtractedField"/> stores.
/// </summary>
/// <remarks>
/// <para>
/// The response is nested and `events` is an array; the store is one row per field name. The
/// bridge is a path convention, and it lives here — in one place, shared by every provider —
/// rather than in each implementation. Two providers inventing slightly different names for
/// the same field is how the accuracy report quietly stops comparing like with like.
/// </para>
/// <para>
/// <strong>The convention:</strong> dotted path from the root, with array position in square
/// brackets. It is the same string the model uses as a <c>field_confidence</c> key, so the
/// confidence for a field is looked up by the field's own name rather than by a parallel
/// structure that can drift out of step.
/// </para>
/// <code>
/// notice_date
/// identifiers.borrower_name
/// events[0].type
/// events[0].dates.payment_due_date
/// events[1].economics.all_in_rate
/// warnings[0]
/// </code>
/// <para>
/// The walk itself is <c>JsonFlatten</c>'s, which produces exactly that path form. What is
/// left here is the part that is ours: which keys are refused, how a confidence is paired to
/// its field, and how a value becomes a typed projection.
/// </para>
/// <para>
/// Nulls are dropped, and so are empty strings and empty containers. A field the notice did
/// not state is absent rather than stored as a row with nothing in it. The schema gives an
/// empty string no meaning — every field is a value or null — so a model returning one has
/// said nothing, and a row with an empty value reads on a review screen exactly like the
/// absence it would be recorded as anyway.
/// </para>
/// </remarks>
public static class ExtractedFieldFlattener
{
    /// <summary>
    /// Two settings, both load-bearing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FloatParseHandling.Decimal"/> because these are money and rates.
    /// Newtonsoft's default parses any number with a decimal point as a <c>double</c>, and
    /// <c>NumericValue</c> is <c>decimal(18,6)</c> — a binary float cannot hold every value
    /// that column can, and the failure is silent and at the far end of the range rather than
    /// on the first test anybody writes. It also happens to preserve a stated scale, so a
    /// model returning <c>0.05320</c> is recorded as <c>0.05320</c> rather than <c>0.0532</c>.
    /// </para>
    /// <para>
    /// <see cref="DateParseHandling.None"/> because a date is a string until this class says
    /// otherwise. Left on, Newtonsoft converts anything date-shaped to a <c>DateTime</c> on
    /// the way in, under its own rules and the ambient culture — which would quietly undo the
    /// deliberate decision below to parse ISO and only ISO.
    /// </para>
    /// </remarks>
    private static readonly JsonSerializerSettings ParseSettings = new()
    {
        FloatParseHandling = FloatParseHandling.Decimal,
        DateParseHandling = DateParseHandling.None,
    };

    /// <summary>Keys that are never stored, whatever a model returns under them.</summary>
    /// <remarks>
    /// <para>
    /// Payment instructions are not economic data and this system does not keep them. The
    /// prompt and the schema both forbid them, so this is the third line of defence rather
    /// than the first — but a model that ignores its schema must not be able to put an account
    /// number into a queryable, indexed table that a review screen renders.
    /// </para>
    /// <para>
    /// Bank names are on the list for a weaker reason and the same effect: a bank routes the
    /// money, it does not describe the economics. The facility is what a notice has to be
    /// matched to. There is no field left in the schema whose name contains
    /// <c>bank_name</c>, so nothing legitimate is caught by it.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenSegments =
    [
        "payment_instructions",
        "aba_routing_number",
        "account_number",
        "swift_code",
        "bank_name",
    ];

    private const string ConfidenceKey = "field_confidence";

    /// <summary>One flattened field, ready to become an <see cref="ExtractedField"/>.</summary>
    /// <param name="FieldName">The dotted path.</param>
    /// <param name="RawValue">What the model said, as text.</param>
    /// <param name="NumericValue">Parsed number, where the value is one.</param>
    /// <param name="DateValue">Parsed date, where the value is an ISO date.</param>
    /// <param name="Confidence">From <c>field_confidence</c>, keyed by the same path.</param>
    public sealed record Field(
        string FieldName,
        string? RawValue,
        decimal? NumericValue,
        DateTime? DateValue,
        decimal? Confidence);

    /// <summary>
    /// Flattens a response body. Throws <see cref="System.Text.Json.JsonException"/> on
    /// malformed JSON, which the caller records as a parse failure with the raw body kept.
    /// </summary>
    public static IReadOnlyList<Field> Flatten(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var root = Parse(json);
        var confidence = ReadConfidence(root);

        // includeNullAndEmptyValues: false drops nulls and empty containers, which is the
        // behaviour we want and would otherwise have to filter for.
        return root.Flatten(includeNullAndEmptyValues: false)
            .Where(entry => !IsConfidence(entry.Key) && !IsForbidden(entry.Key))
            .Select(entry => ToField(entry.Key, entry.Value, confidence))
            .ToList();
    }

    /// <summary>
    /// Newtonsoft is an implementation detail of the walk and does not belong in the contract:
    /// the rest of the codebase is System.Text.Json, and a caller catching a parse failure
    /// should not have to know which library did the parsing to name the exception.
    /// </summary>
    private static JObject Parse(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<JObject>(json, ParseSettings)
                ?? throw new System.Text.Json.JsonException("The response body was not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new System.Text.Json.JsonException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Read from the object rather than from the flattened output, because a confidence key is
    /// itself a path and flattening it produces a key containing a key —
    /// <c>field_confidence['events[0].economics.all_in_rate']</c>. Taking it from the object
    /// keeps the lookup keyed by the plain path the fields use.
    /// </summary>
    private static Dictionary<string, decimal> ReadConfidence(JObject root)
    {
        var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);

        if (root[ConfidenceKey] is not JObject stated) return confidence;

        foreach (var entry in stated.Properties())
        {
            if (entry.Value.Type is JTokenType.Float or JTokenType.Integer)
            {
                confidence[entry.Name] = entry.Value.Value<decimal>();
            }
        }

        return confidence;
    }

    private static Field ToField(string path, object? value, Dictionary<string, decimal> confidence)
    {
        // Booleans lowercased to match the JSON the model actually sent; Newtonsoft's
        // ToString() would write "True".
        var raw = value switch
        {
            bool flag => flag ? "true" : "false",
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString(),
        };

        decimal? numeric = value switch
        {
            decimal number => number,
            long whole => whole,
            int whole => whole,
            _ => null,
        };

        // ISO only, and only exactly. A model emitting "30 September 2026" leaves DateValue
        // null and the text in RawValue, which is a visible discrepancy rather than a date
        // parsed to something plausible under whatever culture the server happens to run in.
        DateTime? date = value is string text
            && DateTime.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            ? parsed
            : null;

        return new Field(
            path,
            raw,
            numeric,
            date,
            confidence.TryGetValue(path, out var stated) ? stated : null);
    }

    /// <summary>
    /// The lookup table is not itself data. Storing it would double every row and give each
    /// copy a name nobody queries.
    /// </summary>
    private static bool IsConfidence(string path) =>
        path.StartsWith(ConfidenceKey, StringComparison.Ordinal);

    private static bool IsForbidden(string path) =>
        ForbiddenSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
}
