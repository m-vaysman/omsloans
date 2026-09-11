using System.Globalization;
using JsonFlatten;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Turns the model's nested response into the flat name/value rows <see cref="ExtractedField"/> stores.
/// </summary>
/// <remarks>
/// Response is nested; <c>events</c> is an array; the store is one row per field name. The bridge
/// is a path convention, here in one place shared by every provider — two providers inventing
/// slightly different names is how the accuracy report stops comparing like with like.
///
/// Convention: dotted path from the root, array position in square brackets. Same string the
/// model uses as a <c>field_confidence</c> key, so confidence looks up by the field's own name.
/// <c>notice_date</c>, <c>events[0].economics.all_in_rate</c>, <c>warnings[0]</c>.
///
/// Nulls, empty strings, and empty containers are dropped. A field the notice did not state is
/// absent, not a row with nothing in it.
/// </remarks>
public static class ExtractedFieldFlattener
{
    /// <summary>
    /// Two settings, both load-bearing.
    /// </summary>
    /// <remarks>
    /// <see cref="FloatParseHandling.Decimal"/> — these are money and rates. Newtonsoft's default
    /// parses decimals as <c>double</c>; <c>NumericValue</c> is <c>decimal(18,6)</c>. A binary float
    /// cannot hold every value that column can; the failure is silent and at the far end of the
    /// range. Also preserves stated scale: <c>0.05320</c> stays <c>0.05320</c>, not <c>0.0532</c>.
    ///
    /// <see cref="DateParseHandling.None"/> — a date is a string until this class says otherwise.
    /// Left on, Newtonsoft converts date-shaped values under ambient culture and undoes the
    /// deliberate ISO-only parse below.
    /// </remarks>
    private static readonly JsonSerializerSettings ParseSettings = new()
    {
        FloatParseHandling = FloatParseHandling.Decimal,
        DateParseHandling = DateParseHandling.None,
    };

    /// <summary>Keys that are never stored, whatever a model returns under them.</summary>
    /// <remarks>
    /// Payment instructions are not economic data and this system does not keep them. Prompt and
    /// schema already forbid them; this is the third line — a model that ignores its schema must
    /// not put an account number into a queryable table a review screen renders.
    ///
    /// Bank names for the same effect: a bank routes money; it does not describe economics. No
    /// remaining schema field contains <c>bank_name</c>, so nothing legitimate is caught.
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
    /// malformed JSON; the caller records a parse failure and keeps the raw body.
    /// </summary>
    public static IReadOnlyList<Field> Flatten(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var root = Parse(json);
        var confidence = ReadConfidence(root);

        // includeNullAndEmptyValues: false drops nulls and empty containers — the behaviour we want.
        return root.Flatten(includeNullAndEmptyValues: false)
            .Where(entry => !IsConfidence(entry.Key) && !IsForbidden(entry.Key))
            .Select(entry => ToField(entry.Key, entry.Value, confidence))
            .ToList();
    }

    /// <summary>
    /// Newtonsoft is an implementation detail of the walk. The rest of the codebase is
    /// System.Text.Json; a caller catching a parse failure should not need to name which library parsed.
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
    /// Read confidence from the object, not the flattened output. Flattening confidence keys
    /// produces <c>field_confidence['events[0].economics.all_in_rate']</c>. From the object, lookup
    /// stays keyed by the plain path the fields use.
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
        // Booleans lowercased to match the JSON the model sent; Newtonsoft ToString() would write "True".
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

        // ISO only, exactly. "30 September 2026" leaves DateValue null and text in RawValue —
        // a visible discrepancy, not a date parsed under whatever culture the server runs.
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
    /// The confidence lookup table is not itself data. Storing it would double every row.
    /// </summary>
    private static bool IsConfidence(string path) =>
        path.StartsWith(ConfidenceKey, StringComparison.Ordinal);

    private static bool IsForbidden(string path) =>
        ForbiddenSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
}
