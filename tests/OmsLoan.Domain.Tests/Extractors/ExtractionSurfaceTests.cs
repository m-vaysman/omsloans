using System.Reflection;
using OmsLoan.Domain.Extractors;

namespace OmsLoan.Domain.Tests.Extractors;

/// <summary>
/// The shape of the public surface, asserted so it cannot be widened by accident.
/// </summary>
/// <remarks>
/// <para>
/// #70's guarantee is that there is exactly one way to run an extraction. That is enforced by
/// access modifiers, and access modifiers are one keyword away from not enforcing it — someone
/// hits "cannot be accessed", types <c>public</c>, and the build goes green with the guarantee
/// gone and nothing in the diff that looks wrong.
/// </para>
/// <para>
/// These tests are that keyword's alarm. They do not test behaviour; they test that the reason
/// the behaviour cannot be bypassed is still in place, and they say why when they fail.
/// </para>
/// </remarks>
public class ExtractionSurfaceTests
{
    private static readonly Assembly Domain = typeof(INoticeExtraction).Assembly;

    private static Type Find(string name) =>
        Domain.GetType($"OmsLoan.Domain.Extractors.{name}", throwOnError: true)!;

    /// <summary>
    /// The entry point is public, because something has to be.
    /// </summary>
    [Fact]
    public void TheEntryPointIsPublic()
    {
        Assert.True(typeof(INoticeExtraction).IsPublic);
        Assert.True(Find("NoticeExtraction").IsPublic);
    }

    /// <summary>
    /// And nothing else that can execute an extraction is reachable from outside.
    /// </summary>
    /// <remarks>
    /// <see cref="INoticeExtractor"/> included. Leaving the interface public would keep one
    /// route open — a keyed resolve straight out of the container — and that route returns a
    /// guarded extractor but bypasses the concurrency gate, which lives in the entry point and
    /// nowhere else.
    /// </remarks>
    [Theory]
    [InlineData("INoticeExtractor")]
    [InlineData("GuardedNoticeExtractor")]
    [InlineData("INoticeExtractorSelector")]
    [InlineData("NoticeExtractorSelector")]
    public void NothingElseThatCanRunAnExtractionIsPublic(string name)
    {
        var type = Find(name);

        Assert.False(
            type.IsPublic,
            $"{name} is public. That reopens a second way to run an extraction, which bypasses "
            + "the concurrency gate in NoticeExtraction. See #70.");
    }

    /// <summary>
    /// The registration helper that wraps a provider in the guard is internal too, so a host
    /// cannot register an unguarded extractor of its own.
    /// </summary>
    [Fact]
    public void TheProviderRegistrationHelperIsInternal()
    {
        var method = Find("NoticeExtractorRegistration")
            .GetMethod("AddNoticeExtractor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.False(
            method.IsPublic,
            "AddNoticeExtractor is public, so a host could register a provider without the guard "
            + "and lose the one-attempt rule, the deadline, and failure-as-a-row. See #70.");
    }

    /// <summary>
    /// No public type anywhere in Domain implements the extractor interface, which is the
    /// general form of the rule above — it catches a new implementation added later that nobody
    /// thought to check.
    /// </summary>
    [Fact]
    public void NoPublicTypeImplementsTheExtractorInterface()
    {
        var offenders = Domain.GetTypes()
            .Where(type => type.IsPublic && typeof(INoticeExtractor).IsAssignableFrom(type) && !type.IsInterface)
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Publicly constructible extractors: {string.Join(", ", offenders)}. Every extractor "
            + "must be reachable only through INoticeExtraction. See #70.");
    }

    /// <summary>
    /// The entry point takes a provider name, because reprocessing and the accuracy report exist
    /// to run the same notice through two providers and compare.
    /// </summary>
    [Fact]
    public void TheEntryPointCanStillNameAProvider()
    {
        var method = typeof(INoticeExtraction).GetMethod(nameof(INoticeExtraction.ExtractAsync));

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), p => p.Name == "providerName" && p.ParameterType == typeof(string));
    }
}
