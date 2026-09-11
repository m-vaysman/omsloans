using Microsoft.Extensions.AI;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Infrastructure.Extraction;

/// <summary>
/// Everything a call needs, assembled and not yet sent.
/// </summary>
/// <remarks>
/// <para>
/// Built as one value before the network so build and send fail separately. A test can
/// inspect prompt, document, media type, schema, model id, sampling — no key, no call, no
/// token spend.
/// </para>
/// <para>
/// If a request was built, everything the provider needs is present; only transport remains.
/// Expensive live tests (schema honour, PDF survival) stay the only ones that need a call.
/// </para>
/// <para>
/// <see cref="Build"/> throws rather than return a half-filled request. A request that exists
/// is complete.
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
    /// Assembles a request, refusing to produce one missing anything.
    /// </summary>
    /// <remarks>
    /// The checks are the value of the type. Each is a way a call could look valid, get paid
    /// for, and come back useless: empty document, schema never in options, empty model id
    /// defaulting to whatever the vendor chooses. Catching them here means a built request
    /// needs no live call to trust.
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
