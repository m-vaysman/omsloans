using System.Text.Json;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The prompt and schema themselves. These are the highest-leverage artifact in the system —
/// a model swap is a configuration change, a bad prompt is wrong output on every provider at
/// once — so the rules that matter most are asserted rather than trusted to review.
/// </summary>
public class ExtractionPromptTests
{
    private static readonly ExtractionPrompt Prompt = PromptCatalog.Extraction;

    [Fact]
    public void ThePromptAndSchemaLoadFromEmbeddedResources()
    {
        Assert.Equal("extraction.v1", Prompt.Version);
        Assert.NotEmpty(Prompt.Text);
        Assert.NotEmpty(Prompt.JsonSchema);
    }

    [Fact]
    public void TheSchemaIsValidJson() => JsonDocument.Parse(Prompt.JsonSchema);

    [Fact]
    public void TheAllowedTypesAreSubstitutedIntoThePrompt()
    {
        Assert.DoesNotContain("{types}", Prompt.Text, StringComparison.Ordinal);

        foreach (var name in NoticeTypes.AllowedNames)
        {
            Assert.Contains(name, Prompt.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The type vocabulary has to agree in three places: the enum, the prompt, and the schema
    /// enum the model is constrained by. A type in one and not the others is a value the model
    /// can emit and nothing downstream can read.
    /// </summary>
    [Fact]
    public void TheSchemaEnumMatchesTheNoticeTypes()
    {
        using var schema = JsonDocument.Parse(Prompt.JsonSchema);

        var allowed = schema.RootElement
            .GetProperty("properties").GetProperty("events")
            .GetProperty("items").GetProperty("properties")
            .GetProperty("type").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(NoticeTypes.AllowedNames, allowed);
    }

    [Theory]
    [InlineData(NoticeType.Unknown, "unknown")]
    [InlineData(NoticeType.RateReset, "rate_reset")]
    [InlineData(NoticeType.InterestPayment, "interest_payment")]
    [InlineData(NoticeType.PrincipalPayment, "principal_payment")]
    [InlineData(NoticeType.Fee, "fee")]
    [InlineData(NoticeType.Rollover, "rollover")]
    [InlineData(NoticeType.Drawdown, "drawdown")]
    [InlineData(NoticeType.CommitmentReduction, "commitment_reduction")]
    public void EveryTypeRoundTripsThroughItsWireName(NoticeType type, string wireName)
    {
        Assert.Equal(wireName, type.WireName());
        Assert.Equal(type, NoticeTypes.Parse(wireName));
    }

    /// <summary>
    /// A model inventing a type must not lose the extraction. It becomes Unknown, which is a
    /// thing a reviewer sees rather than a row nobody can read.
    /// </summary>
    [Theory]
    [InlineData("amortisation")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnrecognisedTypeBecomesUnknownRatherThanThrowing(string? wireName) =>
        Assert.Equal(NoticeType.Unknown, NoticeTypes.Parse(wireName));

    // --- the rules that cost money if they are dropped ------------------------------------

    /// <summary>
    /// Never invent, never compute. A fabricated figure that looks plausible is far more
    /// expensive than a null a reviewer fills in — the review screen exists to catch the
    /// first, and can only do so if the model does not disguise guesses as data.
    /// </summary>
    [Fact]
    public void ThePromptForbidsInventingAndComputingValues()
    {
        Assert.Contains("Never invent", Prompt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never compute", Prompt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all_in_rate", Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptStatesTheNormalisationRules()
    {
        Assert.Contains("0.0532", Prompt.Text, StringComparison.Ordinal);
        Assert.Contains("YYYY-MM-DD", Prompt.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The distinction that a per-type prompt could not express, and the reason this is one
    /// prompt: a paydown and the next period's reset in one document is two events.
    /// </summary>
    [Fact]
    public void ThePromptRequiresOneEventPerEconomicEvent()
    {
        Assert.Contains("one element in \"events\" per distinct economic event", Prompt.Text, StringComparison.Ordinal);

        // Not "Do not smash them": the prompt is hard-wrapped and the phrase straddles a line
        // break. Asserting across a wrap makes the test fail on a reflow that changed nothing.
        Assert.Contains("Do not smash", Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptTellsTheModelToSurfaceAnEventItCannotType()
    {
        Assert.Contains("untyped_event", Prompt.Text, StringComparison.Ordinal);
        Assert.Contains("Do not drop it", Prompt.Text, StringComparison.Ordinal);
    }

    // --- payment instructions, which are never extracted ----------------------------------

    /// <summary>
    /// Bank details are not economic data and this system does not store them. Asserted on the
    /// prompt and the schema separately, because both would have to be edited to reintroduce
    /// them and a test that only checked one would not notice.
    /// </summary>
    [Theory]
    [InlineData("payment_instructions")]
    [InlineData("aba_routing_number")]
    [InlineData("account_number")]
    [InlineData("swift_code")]
    public void NeitherThePromptNorTheSchemaAsksForBankDetails(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Prompt.JsonSchema, StringComparison.OrdinalIgnoreCase);

        // The prompt names them once, in the instruction not to extract them. That single
        // mention must be the prohibition and not a field.
        var mentions = Prompt.Text.Split([forbidden], StringSplitOptions.None).Length - 1;
        Assert.True(mentions <= 1, $"'{forbidden}' appears {mentions} times in the prompt.");
    }

    [Fact]
    public void ThePromptExplicitlyForbidsExtractingPaymentInstructions() =>
        Assert.Contains("Do not extract payment instructions", Prompt.Text, StringComparison.Ordinal);

    /// <summary>
    /// No bank name is asked for at all — not the remittance bank and not the agent. A bank
    /// routes the money; the facility is what the notice has to be matched to. The schema is
    /// the binding half of this (identifiers is additionalProperties:false, so a field that is
    /// not there cannot be returned), and the prompt has to agree or the model is being asked
    /// for something its schema will reject.
    /// </summary>
    [Fact]
    public void NoBankNameIsAskedForIncludingTheAgentBank()
    {
        Assert.DoesNotContain("bank_name", Prompt.JsonSchema, StringComparison.OrdinalIgnoreCase);

        using var schema = JsonDocument.Parse(Prompt.JsonSchema);
        var identifiers = schema.RootElement.GetProperty("properties").GetProperty("identifiers");

        Assert.False(identifiers.GetProperty("additionalProperties").GetBoolean());

        // Not a count of the word "bank" — the prohibition has to say it several times. What
        // must not appear is a *field* the model could fill in.
        Assert.DoesNotContain("bank_name", Prompt.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A version that is not loadable is a row nobody can reproduce. Reprocessing compares an
    /// old extraction against a new one, and that needs the prompt that produced the old row —
    /// not whatever is on disk today.
    /// </summary>
    [Fact]
    public void AnUnknownVersionFailsLoudlyRatherThanFallingBack()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load("extraction.v99"));

        Assert.Contains("extraction.v99", ex.Message, StringComparison.Ordinal);
    }
}
