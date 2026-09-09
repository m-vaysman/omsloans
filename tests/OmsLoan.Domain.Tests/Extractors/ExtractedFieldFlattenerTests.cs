using System.Text.Json;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The bridge between the model's nested answer and the flat rows the store holds.
/// </summary>
/// <remarks>
/// The path convention is a contract in the weakest possible sense — it is a string format
/// agreed between a prompt, a flattener and a review screen, with no compiler holding the
/// three together. These tests are that compiler. A path that changes shape silently does
/// not break a build; it makes the accuracy report compare last month's
/// <c>events[0].economics.all_in_rate</c> against this month's <c>all_in_rate</c> and
/// conclude that the model stopped extracting rates.
/// </remarks>
public class ExtractedFieldFlattenerTests
{
    private static IReadOnlyDictionary<string, ExtractedFieldFlattener.Field> Flatten(string json) =>
        ExtractedFieldFlattener.Flatten(json).ToDictionary(field => field.FieldName, StringComparer.Ordinal);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Extractors", "Fixtures", name));

    // --- the path convention --------------------------------------------------------------

    [Fact]
    public void ARootScalarKeepsItsBareName()
    {
        var fields = Flatten("""{ "notice_date": "2026-09-08" }""");

        Assert.Equal("2026-09-08", fields["notice_date"].RawValue);
    }

    [Fact]
    public void NestingBecomesADottedPath()
    {
        var fields = Flatten("""{ "identifiers": { "borrower_name": "Northstar Packaging Inc" } }""");

        Assert.Equal("Northstar Packaging Inc", fields["identifiers.borrower_name"].RawValue);
    }

    [Fact]
    public void ArrayPositionBecomesASubscript()
    {
        var fields = Flatten("""{ "warnings": ["untyped_event", "conflicting_values:margin"] }""");

        Assert.Equal("untyped_event", fields["warnings[0]"].RawValue);
        Assert.Equal("conflicting_values:margin", fields["warnings[1]"].RawValue);
    }

    /// <summary>
    /// The reason the whole design exists: two events in one document stay two events, and
    /// each keeps its own rates and amounts under its own index.
    /// </summary>
    [Fact]
    public void TwoEventsFlattenToSeparatelySubscriptedPaths()
    {
        var fields = Flatten(Fixture("combined-paydown-and-rate-reset.json"));

        Assert.Equal("principal_payment", fields["events[0].type"].RawValue);
        Assert.Equal(2500000.00m, fields["events[0].economics.principal_amount"].NumericValue);

        Assert.Equal("rate_reset", fields["events[1].type"].RawValue);
        Assert.Equal(0.0756m, fields["events[1].economics.all_in_rate"].NumericValue);

        // And crucially, neither event has picked up the other's figures.
        Assert.False(fields.ContainsKey("events[0].economics.all_in_rate"));
        Assert.False(fields.ContainsKey("events[1].economics.principal_amount"));
    }

    // --- confidence, paired by path -------------------------------------------------------

    /// <summary>
    /// Confidence is looked up by the field's own path rather than carried alongside it in a
    /// parallel structure, which is what stops the two drifting out of step.
    /// </summary>
    [Fact]
    public void ConfidenceIsAttachedToTheFieldItNames()
    {
        var fields = Flatten(Fixture("combined-paydown-and-rate-reset.json"));

        Assert.Equal(0.97m, fields["events[0].economics.principal_amount"].Confidence);
        Assert.Equal(0.93m, fields["events[1].economics.all_in_rate"].Confidence);
    }

    /// <summary>
    /// A field the model did not score is stored without a confidence rather than with a
    /// default. Zero would read as "the model was certain it was wrong" and one would read as
    /// certainty nobody claimed; null reads as what it is.
    /// </summary>
    [Fact]
    public void AFieldWithNoStatedConfidenceGetsNoneRatherThanADefault()
    {
        var fields = Flatten(Fixture("combined-paydown-and-rate-reset.json"));

        Assert.Null(fields["identifiers.borrower_name"].Confidence);
    }

    /// <summary>
    /// The lookup table is not itself data. Storing it would double every row and give the
    /// copy a name nothing queries.
    /// </summary>
    [Fact]
    public void TheConfidenceTableIsNotStoredAsFields()
    {
        var fields = Flatten(Fixture("combined-paydown-and-rate-reset.json"));

        Assert.DoesNotContain(fields.Keys, key => key.StartsWith("field_confidence", StringComparison.Ordinal));
    }

    [Fact]
    public void AConfidenceThatIsNotANumberIsIgnoredRatherThanThrowing()
    {
        var fields = Flatten("""{ "margin": 0.0325, "field_confidence": { "margin": "high" } }""");

        Assert.Null(fields["margin"].Confidence);
    }

    // --- typing ---------------------------------------------------------------------------

    [Fact]
    public void ANumberIsKeptAsTextAndAsANumber()
    {
        var field = Flatten("""{ "margin": 0.0325 }""")["margin"];

        Assert.Equal("0.0325", field.RawValue);
        Assert.Equal(0.0325m, field.NumericValue);
        Assert.Null(field.DateValue);
    }

    [Fact]
    public void AnIsoDateIsParsed()
    {
        var field = Flatten("""{ "notice_date": "2026-09-08" }""")["notice_date"];

        Assert.Equal(new DateTime(2026, 9, 8), field.DateValue);
        Assert.Equal("2026-09-08", field.RawValue);
    }

    /// <summary>
    /// A date the model wrote in prose is a visible discrepancy, not a date guessed at under
    /// whatever culture the server happens to be running in. The text is kept and DateValue
    /// stays null, so a reviewer sees exactly what the model said.
    /// </summary>
    [Theory]
    [InlineData("30 September 2026")]
    [InlineData("09/30/2026")]
    [InlineData("2026-09-30T00:00:00Z")]
    public void ADateThatIsNotIsoIsKeptAsTextAndLeftUnparsed(string stated)
    {
        var field = Flatten($$"""{ "payment_due_date": "{{stated}}" }""")["payment_due_date"];

        Assert.Null(field.DateValue);
        Assert.Equal(stated, field.RawValue);
    }

    [Fact]
    public void ABooleanIsStoredAsText()
    {
        var fields = Flatten("""{ "is_final": true, "is_amended": false }""");

        Assert.Equal("true", fields["is_final"].RawValue);
        Assert.Equal("false", fields["is_amended"].RawValue);
    }

    // --- absence --------------------------------------------------------------------------

    /// <summary>
    /// Most of a notice's fields are null on any given document. Storing them would bury the
    /// dozen fields a notice actually states under sixty that it does not, on every row and
    /// on every review screen.
    /// </summary>
    [Fact]
    public void NullsAreDroppedRatherThanStoredAsEmptyRows()
    {
        var fields = Flatten(Fixture("combined-paydown-and-rate-reset.json"));

        Assert.False(fields.ContainsKey("identifiers.cusip"));
        Assert.False(fields.ContainsKey("events[0].economics.fee_amount"));
    }

    /// <summary>
    /// An empty string goes the same way. The schema gives it no meaning — every field is a
    /// value or null — so a model returning one has said nothing, and a row with an empty
    /// value reads on a review screen exactly like the absence it would otherwise be.
    /// </summary>
    [Fact]
    public void AnEmptyValueIsAbsenceRatherThanAnEmptyRow()
    {
        Assert.Empty(ExtractedFieldFlattener.Flatten("""{ "fee_type": "" }"""));
    }

    [Fact]
    public void AnEmptyArrayContributesNothing()
    {
        Assert.Empty(ExtractedFieldFlattener.Flatten("""{ "warnings": [] }"""));
    }

    // --- payment instructions, which never reach the store --------------------------------

    /// <summary>
    /// The third line of defence, behind the prompt and the schema. Both would have to be
    /// edited to reintroduce bank details — but a model that ignores its schema must not be
    /// able to put an account number into an indexed table that a review screen renders.
    /// </summary>
    [Fact]
    public void AWholePaymentInstructionsBlockIsDropped()
    {
        var fields = Flatten(
            """
            {
              "notice_date": "2026-09-08",
              "payment_instructions": {
                "bank": "First Meridian",
                "aba_routing_number": "021000021",
                "account_number": "4471982203",
                "swift_code": "FMERUS33"
              }
            }
            """);

        Assert.Single(fields);
        Assert.True(fields.ContainsKey("notice_date"));
    }

    /// <summary>
    /// Including when the model does not use the block name — the individual field names are
    /// refused on their own, at whatever depth they appear.
    /// </summary>
    [Theory]
    [InlineData("aba_routing_number")]
    [InlineData("account_number")]
    [InlineData("swift_code")]
    [InlineData("ABA_Routing_Number")]
    public void ABankDetailIsRefusedWhereverItAppears(string forbidden)
    {
        var fields = Flatten($$"""
            { "events": [ { "type": "principal_payment", "{{forbidden}}": "021000021" } ] }
            """);

        Assert.Equal(["events[0].type"], fields.Keys);
    }

    /// <summary>
    /// Including the agent bank. A bank routes the money; it does not describe the economics,
    /// and the facility is what a notice has to be matched to. So it is dropped along with the
    /// remittance details rather than kept as an identifier.
    /// </summary>
    [Fact]
    public void ABankNameIsDroppedEvenWhenItIsTheAgentRatherThanTheRemittanceBank()
    {
        var fields = Flatten(
            """
            {
              "identifiers": {
                "agent_bank_name": "Meridian Trust Agency Services",
                "facility_id": "LN226902"
              }
            }
            """);

        Assert.Equal(["identifiers.facility_id"], fields.Keys);
    }

    // --- malformed input ------------------------------------------------------------------

    /// <summary>
    /// Throws rather than returning nothing. An unparseable body is recorded as a parse
    /// failure with the raw text kept — a row a reviewer can act on. Silently flattening it
    /// to zero fields would look identical to a notice that stated nothing.
    /// </summary>
    [Fact]
    public void MalformedJsonThrowsRatherThanFlatteningToNothing() =>
        // ThrowsAny, not Throws: System.Text.Json raises an internal subclass of JsonException
        // and an exact-type assertion pins a detail of the BCL rather than our behaviour.
        Assert.ThrowsAny<JsonException>(() => ExtractedFieldFlattener.Flatten("{ \"notice_date\": "));

    [Fact]
    public void ANullBodyIsRejectedAtTheBoundary() =>
        Assert.Throws<ArgumentNullException>(() => ExtractedFieldFlattener.Flatten(null!));
}
