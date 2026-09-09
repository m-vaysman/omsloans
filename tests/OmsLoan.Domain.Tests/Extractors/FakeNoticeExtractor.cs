using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// An <see cref="INoticeExtractor"/> that answers from a script instead of a provider.
/// </summary>
/// <remarks>
/// <para>
/// Every behaviour worth testing in the extraction pipeline is a provider behaviour: a 429
/// then a success, a truncated response, a call that never returns. None of those can be
/// arranged against a real vendor on demand, and a test that needs a network and an API key
/// is a test that will be skipped.
/// </para>
/// <para>
/// It lives in the test project rather than in <c>OmsLoan.Domain</c> deliberately — shipping a
/// test double in the production assembly invites it into production code. If the Worker's
/// tests need it once the pipeline lands, it moves to a shared test-support project then,
/// which is a cheaper move than un-shipping it.
/// </para>
/// </remarks>
public sealed class FakeNoticeExtractor(string modelName = "fake-model-1") : INoticeExtractor
{
    private readonly Queue<Func<ExtractionResult>> _script = new();

    public string ModelName { get; } = modelName;

    /// <summary>How many times the provider was actually called.</summary>
    public int Calls { get; private set; }

    /// <summary>The cancellation token handed to the most recent call.</summary>
    public CancellationToken LastToken { get; private set; }

    /// <summary>What every unscripted call returns.</summary>
    public ExtractionResult Default { get; init; } =
        ExtractionResult.Success("fake-model-1", "v1", "{}", new Dictionary<string, string?>(), new ExtractionTelemetry());

    public FakeNoticeExtractor Returns(ExtractionResult result)
    {
        _script.Enqueue(() => result);
        return this;
    }

    public FakeNoticeExtractor Throws(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    /// <summary>Blocks until the call's own token is cancelled — a provider that hangs.</summary>
    public FakeNoticeExtractor Hangs()
    {
        _script.Enqueue(() =>
        {
            LastToken.WaitHandle.WaitOne();
            LastToken.ThrowIfCancellationRequested();
            return Default;
        });

        return this;
    }

    public Task<ExtractionResult> ExtractAsync(
        byte[] pdfBytes,
        NoticeType noticeType,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastToken = cancellationToken;

        var next = _script.Count > 0 ? _script.Dequeue() : () => Default;

        try
        {
            return Task.FromResult(next());
        }
        catch (Exception ex)
        {
            return Task.FromException<ExtractionResult>(ex);
        }
    }
}
