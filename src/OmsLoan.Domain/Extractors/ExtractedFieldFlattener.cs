using System.Globalization;
using System.Text.Json;

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
/// Nulls are dropped. A field the notice did not state is absent rather than stored as an
/// empty row — but the distinction between "not stated" and "stated as empty" is preserved,
/// because the model is instructed to use null for the first and would have to emit a string
/// for the second.
/// </para>
/// </remarks>
public static class ExtractedFieldFlattener
{
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
    /// Flattens a response body. Throws <see cref="JsonException"/> on malformed JSON, which
    /// the caller records as a parse failure with the raw body kept.
    /// </summary>
    public static IReadOnlyList<Field> Flatten(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);

        var confidence = ReadConfidence(document.RootElement);
        var fields = new List<Field>();

        Walk(document.RootElement, path: string.Empty, fields, confidence);

        return fields;
    }

    private static Dictionary<string, decimal> ReadConfidence(JsonElement root)
    {
        var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("field_confidence", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return confidence;
        }

        foreach (var entry in element.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDecimal(out var value))
            {
                confidence[entry.Name] = value;
            }
        }

        return confidence;
    }

    private static void Walk(
        JsonElement element,
        string path,
        List<Field> fields,
        Dictionary<string, decimal> confidence)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    // field_confidence is the lookup table, not data. Storing it as fields
                    // would double every row and give each one a name nobody queries.
                    if (property.NameEquals("field_confidence"))
                    {
                        continue;
                    }

                    if (IsForbidden(property.Name))
                    {
                        continue;
                    }

                    Walk(property.Value, Join(path, property.Name), fields, confidence);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{index++}]", fields, confidence);
                }

                break;

            case JsonValueKind.Null:
                // Not stated. Absent rather than an empty row.
                break;

            default:
                if (IsForbidden(path))
                {
                    break;
                }

                fields.Add(Scalar(element, path, confidence));
                break;
        }
    }

    private static Field Scalar(JsonElement element, string path, Dictionary<string, decimal> confidence)
    {
        var raw = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText(),
        };

        decimal? numeric = element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value)
            ? value
            : null;

        // ISO only, and only exactly. A model emitting "30 September 2026" leaves DateValue
        // null and the text in RawValue, which is a visible discrepancy rather than a date
        // parsed to something plausible under whatever culture the server happens to run in.
        DateTime? date = element.ValueKind == JsonValueKind.String
            && DateTime.TryParseExact(
                raw,
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

    private static bool IsForbidden(string nameOrPath) =>
        ForbiddenSegments.Any(segment =>
            nameOrPath.Contains(segment, StringComparison.OrdinalIgnoreCase));

    private static string Join(string path, string name) =>
        path.Length == 0 ? name : $"{path}.{name}";
}
