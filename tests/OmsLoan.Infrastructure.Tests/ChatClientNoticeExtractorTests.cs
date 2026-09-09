using System.Text.Json;
using Microsoft.Extensions.AI;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// The one extractor, standing in for the three that #8, #9 and #10 originally asked for.
/// </summary>
/// <remarks>
/// These assert against a fake <see cref="IChatClient"/> rather than a live provider, so they
/// cover the half we control: what leaves the process, and what is made of what comes back.
/// Whether a given vendor honours the schema it is sent is a question only a real call can
/// answer, and it is left to the spike on #65.
/// </remarks>
public class ChatClientNoticeExtractorTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 pretend"u8.ToArray();

    private static ProviderOptions Options(bool native = true, int maxTokens = 4096, int maxBytes = 30 * 1024 * 1024) =>
        new()
        {
            ApiKey = "k",
            ModelId = "claude-sonnet-4-6",
            MaxTokens = maxTokens,
            SendsPdfNatively = native,
            MaxDocumentBytes = maxBytes,
        };

    private static ChatClientNoticeExtractor Build(
        FakeChatClient client,
        ProviderOptions? options = null,
        IPdfTextExtractor? text = null) =>
        new(client, options ?? Options(), text);

    private static string ValidResponse => """
        {
          "identifiers": { "borrower_name": "Northstar Packaging Inc", "currency": "USD" },
          "notice_date": "2026-09-08",
          "events": [
            { "type": "principal_payment",
              "dates": { "payment_due_date": "2026-09-30" },
              "economics": { "principal_amount": 2500000.00 },
              "warnings": [] }
          ],
          "field_confidence": { "events[0].economics.principal_amount": 0.97 },
          "warnings": []
        }
        """;

    // --- what leaves the process ------------------------------------------------------------

    /// <summary>
    /// #8's first criterion, and the reason ADR 0001 chose Claude. The PDF goes as a document,
    /// not as text somebody extracted first — a rate table flattened to text loses which
    /// tranche owns which rate, and no prompt recovers that.
    /// </summary>
    [Fact]
    public async Task ThePdfIsSentAsADocumentNotAsExtractedText()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        var document = client.ContentOfType<DataContent>();

        Assert.NotNull(document);
        Assert.Equal("application/pdf", document.MediaType);
        Assert.True(document.Data.Span.SequenceEqual(Pdf));
    }

    /// <summary>
    /// The schema reaches the provider, rather than being described to it in the prompt. This
    /// is the layer a model cannot talk its way past: every object is
    /// <c>additionalProperties: false</c>, so there is no field for an account number to
    /// arrive in.
    /// </summary>
    [Fact]
    public async Task TheJsonSchemaIsAttachedToTheRequest()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        var format = Assert.IsType<ChatResponseFormatJson>(client.LastOptions?.ResponseFormat);

        Assert.NotNull(format.Schema);

        var schema = format.Schema!.Value;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }

    /// <summary>
    /// The model id comes from configuration on every call. #8 asks for it pinned and never
    /// hardcoded, because two rows carrying the same model name and different behaviour would
    /// make the accuracy report unreadable.
    /// </summary>
    [Fact]
    public async Task TheModelIdComesFromConfiguration()
    {
        var client = new FakeChatClient().Returns(ValidResponse);
        var options = new ProviderOptions { ApiKey = "k", ModelId = "gpt-5-mini" };

        var result = await Build(client, options).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal("gpt-5-mini", client.LastOptions?.ModelId);
        Assert.Equal("gpt-5-mini", result.ModelName);
    }

    /// <summary>
    /// Zero temperature, so a second run of the same notice differs because the model changed
    /// rather than because sampling did. Reprocessing compares extractions; sampling noise
    /// would show up in that comparison as a model regression.
    /// </summary>
    [Fact]
    public async Task SamplingIsDeterministic()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(0f, client.LastOptions?.Temperature);
    }

    // --- the text path, which is Groq's ------------------------------------------------------

    /// <summary>
    /// #10: a provider that cannot take documents gets text, and the fact that it did is the
    /// thing that has to be visible rather than hidden.
    /// </summary>
    [Fact]
    public async Task AProviderThatCannotTakeDocumentsIsSentText()
    {
        var client = new FakeChatClient().Returns(ValidResponse);
        var text = new StubTextExtractor("NOTICE OF PRINCIPAL PAYMENT ...");

        await Build(client, Options(native: false), text).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Null(client.ContentOfType<DataContent>());
        Assert.Contains("NOTICE OF PRINCIPAL PAYMENT", client.AllText(), StringComparison.Ordinal);
        Assert.Equal(1, text.Calls);
    }

    /// <summary>
    /// And a provider configured that way with nothing to do the extraction fails saying so,
    /// rather than sending an empty document and getting back a confident extraction of a
    /// notice nobody read.
    /// </summary>
    [Fact]
    public async Task AProviderNeedingTextWithNoExtractorFailsClearly()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        var ex = await Assert.ThrowsAsync<ExtractionProviderException>(
            () => Build(client, Options(native: false), text: null).ExtractAsync(Pdf, NoticeType.Unknown, default));

        Assert.Contains("no text extractor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.Calls);
    }

    // --- what is made of the answer -----------------------------------------------------------

    [Fact]
    public async Task ASuccessFlattensToFieldsAndKeepsTheBody()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        var result = await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.Succeeded, result.Outcome);
        Assert.Equal(ValidResponse, result.RawJson);
        Assert.Equal("2500000.00", result.Fields["events[0].economics.principal_amount"]);
        Assert.Equal("Northstar Packaging Inc", result.Fields["identifiers.borrower_name"]);
        Assert.Equal(PromptCatalog.Extraction.Version, result.PromptVersion);
    }

    /// <summary>
    /// A truncated answer is a failure, and this is the criterion both #8 and #9 spell out.
    /// The danger is specific: a response cut off mid-object parses as far as it got, and the
    /// fields that never arrived are indistinguishable from fields the notice did not state.
    /// Recorded as a success it would be silently short an amount.
    /// </summary>
    [Fact]
    public async Task ATruncatedResponseIsAFailureRatherThanAPartialSuccess()
    {
        var client = new FakeChatClient().Returns(ValidResponse, ChatFinishReason.Length);

        var result = await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ParseFailed, result.Outcome);
        Assert.Equal(ValidResponse, result.RawJson);
        Assert.Contains("token cap", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A body that will not parse is kept. It is the evidence a reviewer needs, and the only
    /// thing separating a model that answered badly from one that did not answer.
    /// </summary>
    [Fact]
    public async Task AMalformedBodyIsStoredAndMarkedFailed()
    {
        var client = new FakeChatClient().Returns("{ \"events\": ");

        var result = await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ParseFailed, result.Outcome);
        Assert.Equal("{ \"events\": ", result.RawJson);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AnEmptyResponseIsAFailureNamingTheFinishReason()
    {
        var client = new FakeChatClient().Returns(string.Empty, new ChatFinishReason("content_filter"));

        var result = await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ParseFailed, result.Outcome);
        Assert.Contains("content_filter", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tokens per call, which is what makes cost per notice a number rather than a monthly
    /// surprise.
    /// </summary>
    [Fact]
    public async Task TokenCountsAndFinishReasonAreRecorded()
    {
        var client = new FakeChatClient().Returns(ValidResponse, ChatFinishReason.Stop, input: 12_400, output: 830);

        var result = await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(12_400, result.Telemetry.PromptTokens);
        Assert.Equal(830, result.Telemetry.CompletionTokens);
        Assert.Equal(13_230, result.Telemetry.TotalTokens);
        Assert.Equal("stop", result.Telemetry.FinishReason);
    }

    /// <summary>
    /// #8's last criterion. An oversized notice fails saying it is oversized, before the call,
    /// rather than as whatever transport error the vendor returns for a request that was too
    /// big — which reads like an outage and sends whoever sees it to the wrong place.
    /// </summary>
    [Fact]
    public async Task AnOversizedNoticeFailsWithAnActionableErrorAndIsNotSent()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        var result = await Build(client, Options(maxBytes: 4)).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(ExtractionOutcome.ProviderFailed, result.Outcome);
        Assert.Contains("accepts", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.Calls);
    }

    /// <summary>
    /// The extractor asks once. Retries and the deadline belong to GuardedNoticeExtractor,
    /// which wraps every registration — one place, so no provider can quietly add a second
    /// policy underneath the one that says there are none.
    /// </summary>
    [Fact]
    public async Task TheProviderIsAskedExactlyOnce()
    {
        var client = new FakeChatClient().Returns(ValidResponse);

        await Build(client).ExtractAsync(Pdf, NoticeType.Unknown, default);

        Assert.Equal(1, client.Calls);
    }

    private sealed class StubTextExtractor(string text) : IPdfTextExtractor
    {
        public int Calls { get; private set; }

        public string Extract(byte[] pdfBytes)
        {
            Calls++;
            return text;
        }
    }
}
