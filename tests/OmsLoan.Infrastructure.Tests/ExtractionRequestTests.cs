using System.Text.Json;
using Microsoft.Extensions.AI;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// The request, assembled and inspected without anything being sent.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that cost nothing. No key, no network, no tokens — and between them
/// they cover every way a call could go out looking valid and come back useless having been
/// paid for.
/// </para>
/// <para>
/// That is the trade the split is for. Once building and sending are separate, a request that
/// was built successfully is one whose only remaining risk is transport, so the questions that
/// genuinely need a live call — does this vendor honour this schema, does a PDF survive to
/// that model — are the only ones anybody has to pay to answer.
/// </para>
/// </remarks>
public class ExtractionRequestTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 pretend"u8.ToArray();

    private static ChatClientNoticeExtractor Native() =>
        new(new FakeChatClient(), new ProviderOptions { ApiKey = "k", ModelId = "claude-sonnet-4-6" });

    private static ChatClientNoticeExtractor Text(string extracted = "NOTICE OF PRINCIPAL PAYMENT") =>
        new(new FakeChatClient(),
            new ProviderOptions { ApiKey = "k", ModelId = "openai/gpt-oss-120b", SendsPdfNatively = false },
            new StubText(extracted));

    // --- a built request is a complete one ----------------------------------------------------

    [Fact]
    public void ANativeRequestCarriesTheInstructionsAndTheDocument()
    {
        var request = Native().BuildRequest(Pdf);

        var contents = request.Message.Contents;

        Assert.Equal(ChatRole.User, request.Message.Role);
        Assert.Contains(contents.OfType<TextContent>(), t => t.Text.Contains("Never invent", StringComparison.OrdinalIgnoreCase));

        var document = Assert.Single(contents.OfType<DataContent>());

        Assert.Equal("application/pdf", document.MediaType);
        Assert.True(document.Data.Span.SequenceEqual(Pdf));
        Assert.Equal(DocumentMode.Native, request.DocumentMode);
    }

    [Fact]
    public void ATextRequestCarriesTheNoticeAsTextAndNoDocument()
    {
        var request = Text().BuildRequest(Pdf);

        Assert.Empty(request.Message.Contents.OfType<DataContent>());
        Assert.Contains(
            request.Message.Contents.OfType<TextContent>(),
            t => t.Text.Contains("NOTICE OF PRINCIPAL PAYMENT", StringComparison.Ordinal));
        Assert.Equal(DocumentMode.ExtractedText, request.DocumentMode);
    }

    /// <summary>
    /// The schema is in the options, not only in the prose. Without it the model is asked for
    /// a shape rather than held to one, and the layer that keeps bank details out of the
    /// database becomes advice.
    /// </summary>
    [Fact]
    public void TheSchemaIsAttachedAndIsClosed()
    {
        var request = Native().BuildRequest(Pdf);

        var format = Assert.IsType<ChatResponseFormatJson>(request.Options.ResponseFormat);

        Assert.NotNull(format.Schema);
        Assert.False(format.Schema!.Value.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(JsonValueKind.Object, format.Schema!.Value.ValueKind);
    }

    [Fact]
    public void TheModelAndCapsComeFromConfiguration()
    {
        var extractor = new ChatClientNoticeExtractor(
            new FakeChatClient(),
            new ProviderOptions { ApiKey = "k", ModelId = "gpt-5-mini", MaxTokens = 2048 });

        var request = extractor.BuildRequest(Pdf);

        Assert.Equal("gpt-5-mini", request.Options.ModelId);
        Assert.Equal(2048, request.Options.MaxOutputTokens);
        Assert.Equal(0f, request.Options.Temperature);
    }

    [Fact]
    public void ThePromptVersionTravelsWithTheRequest() =>
        Assert.Equal(PromptCatalog.Extraction.Version, Native().BuildRequest(Pdf).Prompt.Version);

    // --- and an incomplete one is never produced ----------------------------------------------

    /// <summary>
    /// The guarantee stated as a test: a request with nothing to read cannot be built. Sent, it
    /// would have asked the model to extract from nothing, and the model would have answered
    /// with a full set of nulls — a paid-for call returning a plausible empty extraction.
    /// </summary>
    [Fact]
    public void ARequestWithNoNoticeInItCannotBeBuilt()
    {
        var ex = Assert.Throws<ExtractionProviderException>(() =>
            ExtractionRequest.Build(
                PromptCatalog.Extraction,
                [new TextContent(PromptCatalog.Extraction.Text)],
                new ChatOptions { ModelId = "m", ResponseFormat = Schema() },
                DocumentMode.Native));

        Assert.Contains("no notice to read", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARequestWithNoSchemaCannotBeBuilt()
    {
        var ex = Assert.Throws<ExtractionProviderException>(() =>
            ExtractionRequest.Build(
                PromptCatalog.Extraction,
                [new TextContent("do the thing"), new DataContent(Pdf, "application/pdf")],
                new ChatOptions { ModelId = "m" },
                DocumentMode.Native));

        Assert.Contains("schema is not attached", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARequestWithNoModelIdCannotBeBuilt()
    {
        var ex = Assert.Throws<ExtractionProviderException>(() =>
            ExtractionRequest.Build(
                PromptCatalog.Extraction,
                [new TextContent("do the thing"), new DataContent(Pdf, "application/pdf")],
                new ChatOptions { ResponseFormat = Schema() },
                DocumentMode.Native));

        Assert.Contains("model id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARequestWithNoInstructionsCannotBeBuilt()
    {
        var ex = Assert.Throws<ExtractionProviderException>(() =>
            ExtractionRequest.Build(
                PromptCatalog.Extraction,
                [new TextContent("   "), new DataContent(Pdf, "application/pdf")],
                new ChatOptions { ModelId = "m", ResponseFormat = Schema() },
                DocumentMode.Native));

        Assert.Contains("no instructions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- preflight: refused before anything is built ------------------------------------------

    [Fact]
    public void AnEmptyNoticeIsRefusedBeforeAnythingIsBuilt() =>
        Assert.Contains("empty", Native().Preflight([])!, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void AnOversizedNoticeIsRefusedBeforeAnythingIsBuilt()
    {
        var extractor = new ChatClientNoticeExtractor(
            new FakeChatClient(),
            new ProviderOptions { ApiKey = "k", ModelId = "m", MaxDocumentBytes = 4 });

        Assert.Contains("accepts", extractor.Preflight(Pdf)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWorkableNoticeIsNotRefused() => Assert.Null(Native().Preflight(Pdf));

    private static ChatResponseFormat Schema()
    {
        using var document = JsonDocument.Parse(PromptCatalog.Extraction.JsonSchema);

        return ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), "notice_extraction", "test");
    }

    private sealed class StubText(string text) : IPdfTextExtractor
    {
        public string Extract(byte[] pdfBytes) => text;
    }
}
