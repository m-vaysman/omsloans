using System.Text.Json;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The frozen regression baseline: eight generated notices, each with the extraction it
/// should produce.
/// </summary>
/// <remarks>
/// <para>
/// No provider exists yet, so nothing here compares a model's answer against these. What
/// these tests do is check the answer key itself — that every expected file is loadable, in
/// the shape the schema describes, drawn from the type vocabulary the enum knows, and free of
/// the fields this system refuses to store.
/// </para>
/// <para>
/// That matters more than it sounds. An answer key nobody validates is the one place a bug
/// does the most damage: a wrong expectation makes a correct extraction look like a
/// regression, and the natural response to a failing accuracy run is to change the prompt.
/// </para>
/// </remarks>
public class NoticeCorpusTests
{
    private static readonly string CorpusRoot =
        Path.Combine(AppContext.BaseDirectory, "Extractors", "Fixtures", "corpus");

    public static TheoryData<string> ExpectedFiles()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.GetFiles(CorpusRoot, "*.expected.json"))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    private static JsonDocument Load(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusRoot, fileName)));

    /// <summary>
    /// A corpus that quietly emptied itself would make every test below vacuous, and they
    /// would all still pass.
    /// </summary>
    [Fact]
    public void TheBaselineIsPresentAndEveryPdfHasItsAnswer()
    {
        var pdfs = Directory.GetFiles(CorpusRoot, "*.pdf");

        Assert.Equal(8, pdfs.Length);

        foreach (var pdf in pdfs)
        {
            var expected = pdf[..^".pdf".Length] + ".expected.json";

            Assert.True(File.Exists(expected), $"{Path.GetFileName(pdf)} has no expected output.");
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedFiles))]
    public void EveryExpectedFileFlattensToFieldsWithUsablePaths(string fileName)
    {
        var fields = ExtractedFieldFlattener.Flatten(File.ReadAllText(Path.Combine(CorpusRoot, fileName)));

        Assert.NotEmpty(fields);
        Assert.Contains(fields, field => field.FieldName.StartsWith("events[0].", StringComparison.Ordinal));
        Assert.Contains(fields, field => field.FieldName == "notice_date");
        Assert.Contains(fields, field => field.FieldName == "identifiers.borrower_name");
    }

    /// <summary>
    /// A type the enum cannot parse is a row nothing downstream can read, and an answer key
    /// full of them would score every extraction wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpectedFiles))]
    public void EveryEventTypeIsOneTheEnumKnows(string fileName)
    {
        using var expected = Load(fileName);

        foreach (var element in expected.RootElement.GetProperty("events").EnumerateArray())
        {
            var wireName = element.GetProperty("type").GetString();

            Assert.Contains(wireName, NoticeTypes.AllowedNames);
            Assert.NotEqual(NoticeType.Unknown, NoticeTypes.Parse(wireName));
        }
    }

    /// <summary>
    /// The generator prints a full set of bank details on every page. None of it may appear
    /// in the answer, or the corpus would be teaching the opposite of the rule.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpectedFiles))]
    public void NoExpectedOutputContainsBankDetails(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(CorpusRoot, fileName));

        // Full key names rather than fragments. A bare "aba" would have failed on a borrower
        // called Alabama Steel — a test that fires on the roster rather than on a leak.
        foreach (var forbidden in new[]
                 {
                     "payment_instructions", "aba_routing_number", "account_number",
                     "swift_code", "bank_name",
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        // And nothing under a key this system does not recognise at all, whatever it is named.
        foreach (var field in ExtractedFieldFlattener.Flatten(json))
        {
            Assert.DoesNotContain("bank", field.FieldName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("account", field.FieldName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Rates are decimals, not percentages. An answer key holding 5.37 where the model is
    /// told to return 0.0537 would fail every correct extraction — and 5.37 is exactly what
    /// appears on the page, so this is the plausible mistake rather than a far-fetched one.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpectedFiles))]
    public void RatesAreNormalisedToDecimals(string fileName)
    {
        using var expected = Load(fileName);

        foreach (var element in expected.RootElement.GetProperty("events").EnumerateArray())
        {
            var economics = element.GetProperty("economics");

            foreach (var name in new[] { "base_rate", "margin", "all_in_rate" })
            {
                var rate = economics.GetProperty(name);

                if (rate.ValueKind == JsonValueKind.Null) continue;

                Assert.InRange(rate.GetDecimal(), 0m, 0.5m);
            }
        }
    }

    // --- the two fixtures that exist for a specific rule -----------------------------------

    /// <summary>
    /// Never compute. The notice states a base rate and a margin and withholds the total, so
    /// the correct answer leaves all_in_rate null even though the arithmetic is trivial.
    /// </summary>
    [Fact]
    public void TheNeverComputeFixtureStatesTheComponentsAndNotTheTotal()
    {
        using var expected = Load("003-rate-reset-no-all-in-t3.expected.json");

        var economics = expected.RootElement.GetProperty("events")[0].GetProperty("economics");

        Assert.NotEqual(JsonValueKind.Null, economics.GetProperty("base_rate").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, economics.GetProperty("margin").ValueKind);
        Assert.Equal(JsonValueKind.Null, economics.GetProperty("all_in_rate").ValueKind);
    }

    /// <summary>
    /// Two events in one document, and neither carrying the other's figures. This is the case
    /// a prompt-per-notice-type could not express and the reason there is one prompt.
    /// </summary>
    [Fact]
    public void TheCombinedFixtureKeepsThePaydownAndTheResetApart()
    {
        using var expected = Load("005-combined-paydown-and-rate-reset-t5.expected.json");

        var events = expected.RootElement.GetProperty("events");

        Assert.Equal(2, events.GetArrayLength());

        var paydown = events[0];
        var reset = events[1];

        Assert.Equal("principal_payment", paydown.GetString("type"));
        Assert.Equal("rate_reset", reset.GetString("type"));

        // The paydown has an amount and no rate.
        Assert.NotEqual(JsonValueKind.Null, paydown.GetProperty("economics").GetProperty("principal_amount").ValueKind);
        Assert.Equal(JsonValueKind.Null, paydown.GetProperty("economics").GetProperty("all_in_rate").ValueKind);

        // The reset has a rate and no amount.
        Assert.NotEqual(JsonValueKind.Null, reset.GetProperty("economics").GetProperty("all_in_rate").ValueKind);
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("economics").GetProperty("principal_amount").ValueKind);
    }

    /// <summary>
    /// And the flattener gives them distinct paths, which is what keeps them apart once they
    /// are rows rather than JSON.
    /// </summary>
    [Fact]
    public void TheCombinedFixtureFlattensToTwoIndexedEvents()
    {
        var fields = ExtractedFieldFlattener
            .Flatten(File.ReadAllText(Path.Combine(CorpusRoot, "005-combined-paydown-and-rate-reset-t5.expected.json")))
            .ToDictionary(field => field.FieldName, StringComparer.Ordinal);

        Assert.Equal("principal_payment", fields["events[0].type"].RawValue);
        Assert.Equal("rate_reset", fields["events[1].type"].RawValue);

        Assert.True(fields.ContainsKey("events[0].economics.principal_amount"));
        Assert.False(fields.ContainsKey("events[0].economics.all_in_rate"));

        Assert.True(fields.ContainsKey("events[1].economics.all_in_rate"));
        Assert.False(fields.ContainsKey("events[1].economics.principal_amount"));
    }
}

internal static class JsonElementExtensions
{
    public static string? GetString(this JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString();
}
