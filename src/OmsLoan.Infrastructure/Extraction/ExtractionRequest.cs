using Microsoft.Extensions.AI;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Infrastructure.Extraction;

/// <summary>
/// Everything a call needs, assembled and not yet sent.
/// </summary>
/// <remarks>
/// <para>
/// The whole request is built as one value before anything goes near the network, so that
/// building it and sending it are separate things that can fail separately. That split is the
/// point: a test can assemble a request and inspect every part of it — the prompt, the
/// document, its media type, the schema, the model id, the sampling — without a key, without a
/// network call, and without spending a token.
/// </para>
/// <para>
/// What that buys is a guarantee in the other direction. If a request was built, everything
/// the provider needs is present and correct; the only thing that can still go wrong is
/// transport. So the expensive tests — does this vendor honour this schema, does a PDF survive
/// to that model — are the only ones that need a real call, and everything else is free.
/// </para>
/// <para>
/// <see cref="Build"/> throws rather than returning a half-filled request. A request that
/// exists is a complete one.
/// </para>
/// </remarks>
public sealed record ExtractionRequest
{
    private ExtractionRequest(
        ChatMessage message,
        ChatOptions options,
        DocumentMode documentMode,
        ExtractionPrompt prompt)
    {
        Message = message;
        Options = options;
        DocumentMode = documentMode;
        Prompt = prompt;
    }

    /// <summary>
    /// Assembles a request, refusing to produce one that is missing anything.
    /// </summary>
    /// <remarks>
    /// The checks look paranoid and are the whole value of the type. Each one is a way a call
    /// could go out looking valid and come back useless, having been paid for: a document with
    /// no notice in it, a schema that never made it into the options so the model was only
    /// asked nicely, a model id that was left empty and defaulted to whatever the vendor
    /// chooses. Catching them here means a built request needs no live call to trust.
    /// </remarks>
    internal static ExtractionRequest Build(
        ExtractionPrompt prompt,
        IList<AIContent> contents,
        ChatOptions options,
        DocumentMode documentMode)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(options);

        if (contents.Count == 0)
        {
            throw new ExtractionProviderException("The request has no content: nothing would be sent.");
        }

        if (!contents.OfType<TextContent>().Any(text => !string.IsNullOrWhiteSpace(text.Text)))
        {
            throw new ExtractionProviderException("The request carries no instructions.");
        }

        var carriesNotice = documentMode == DocumentMode.Native
            ? contents.OfType<DataContent>().Any()
            : contents.OfType<TextContent>().Count() > 1;

        if (!carriesNotice)
        {
            throw new ExtractionProviderException(
                $"The request carries no notice to read ({documentMode}). The model would have "
                + "been asked to extract from nothing and would have answered with nulls.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new ExtractionProviderException(
                "No model id, so the row could not say which model produced it.");
        }

        if (options.ResponseFormat is not ChatResponseFormatJson { Schema: not null })
        {
            throw new ExtractionProviderException(
                "The schema is not attached to the request. Without it the model is asked for a "
                + "shape rather than held to one, and the layer that keeps bank details out of "
                + "the database becomes advice.");
        }

        return new ExtractionRequest(new ChatMessage(ChatRole.User, contents), options, documentMode, prompt);
    }

    /// <summary>The single user message: the instructions, and the notice.</summary>
    public ChatMessage Message { get; }

    /// <summary>Model, token cap, sampling, and the schema the response is constrained to.</summary>
    public ChatOptions Options { get; }

    /// <summary>Whether the notice went as a document or as text, recorded on the extraction.</summary>
    public DocumentMode DocumentMode { get; }

    /// <summary>The prompt and schema pair this was built from, by version.</summary>
    public ExtractionPrompt Prompt { get; }
}
