using Microsoft.Extensions.AI;

namespace OmsLoan.Infrastructure.Tests;

/// <summary>
/// A stand-in provider that records what it was asked and answers what it was told to.
/// </summary>
/// <remarks>
/// The point of these tests is the request as much as the response. Whether a PDF left as a
/// document or as flattened text, and whether the schema was actually attached to the call,
/// are not observable from the returned <c>ExtractionResult</c> — and they are two of the
/// three things #8 and #9 are really about.
/// </remarks>
public sealed class FakeChatClient : IChatClient
{
    private ChatResponse _response = new(new ChatMessage(ChatRole.Assistant, "{}"));
    private Exception? _throws;

    public int Calls { get; private set; }

    /// <summary>The messages from the most recent call.</summary>
    public IList<ChatMessage> LastMessages { get; private set; } = [];

    /// <summary>The options from the most recent call.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public FakeChatClient Returns(string json, ChatFinishReason? finishReason = null, long? input = null, long? output = null)
    {
        _response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            FinishReason = finishReason ?? ChatFinishReason.Stop,
            Usage = input is null && output is null
                ? null
                : new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        };

        return this;
    }

    public FakeChatClient Throws(Exception exception)
    {
        _throws = exception;
        return this;
    }

    /// <summary>The single content block of the given type from the last user message.</summary>
    public T? ContentOfType<T>() where T : AIContent =>
        LastMessages.SelectMany(message => message.Contents).OfType<T>().FirstOrDefault();

    public string AllText() =>
        string.Concat(LastMessages.SelectMany(m => m.Contents).OfType<TextContent>().Select(c => c.Text));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastMessages = messages.ToList();
        LastOptions = options;

        if (_throws is not null) throw _throws;

        return Task.FromResult(_response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Extraction does not stream: the whole body is persisted before parsing.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
