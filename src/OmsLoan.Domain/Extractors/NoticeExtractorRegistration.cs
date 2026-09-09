using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// Resolves an extractor by provider name.
/// </summary>
/// <remarks>
/// The thing that makes provider choice a value rather than a code path — and therefore what
/// the reprocess command (#18) and the accuracy report (#19) are built on. Running the same
/// notice through two providers has to be a loop over names, not a branch.
/// </remarks>
public interface INoticeExtractorSelector
{
    /// <summary>Providers that are registered, in configuration order.</summary>
    IReadOnlyList<string> Available { get; }

    /// <summary>
    /// The extractor for a provider, or the configured default when no name is given.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The name is not registered. Thrown rather than falling back to the default: a
    /// reprocess run asked for one provider and silently given another produces rows labelled
    /// with a model that never saw the notice, which is worse than not running.
    /// </exception>
    INoticeExtractor Get(string? providerName = null);

    /// <summary>The extractor for a provider, or null when it is not registered.</summary>
    INoticeExtractor? TryGet(string providerName);
}

/// <inheritdoc />
public sealed class NoticeExtractorSelector(
    IServiceProvider services,
    IOptions<ExtractionOptions> options) : INoticeExtractorSelector
{
    private readonly ExtractionOptions _options = options.Value;

    public IReadOnlyList<string> Available => _options.ConfiguredProviders;

    public INoticeExtractor Get(string? providerName = null)
    {
        var name = string.IsNullOrWhiteSpace(providerName) ? _options.DefaultProvider : providerName;

        return TryGet(name)
            ?? throw new InvalidOperationException(
                $"No extraction provider named '{name}' is registered. "
                + $"Configured: {(Available.Count == 0 ? "(none)" : string.Join(", ", Available))}. "
                + "A provider needs both an API key and a model id before it is registered.");
    }

    public INoticeExtractor? TryGet(string providerName) =>
        services.GetKeyedService<INoticeExtractor>(providerName);
}

/// <summary>
/// Registers the extraction seam. Providers add themselves; this puts the plumbing in place.
/// </summary>
public static class NoticeExtractorRegistration
{
    /// <summary>
    /// Registers one provider under its name, wrapped in
    /// <see cref="ResilientNoticeExtractor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed, so a caller asks for a provider by the name it has in configuration. Every
    /// registration is wrapped, so no implementation has to remember to handle a 429 and none
    /// of them can get the policy subtly different from the others.
    /// </para>
    /// <para>
    /// An unconfigured provider — no key, or no model id — is <em>not</em> registered at all.
    /// Registering it would mean a caller could resolve something that is certain to fail on
    /// its first call, and the failure would look like a provider outage rather than a
    /// deployment that was never finished.
    /// </para>
    /// </remarks>
    /// <returns>True when the provider was registered; false when it is not configured.</returns>
    public static bool AddNoticeExtractor(
        this IServiceCollection services,
        ExtractionOptions extraction,
        string providerName,
        Func<IServiceProvider, ProviderOptions, INoticeExtractor> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(factory);

        if (!extraction.Providers.TryGetValue(providerName, out var provider) || !provider.IsConfigured)
        {
            return false;
        }

        services.AddKeyedSingleton<INoticeExtractor>(
            providerName,
            (serviceProvider, _) => new ResilientNoticeExtractor(
                factory(serviceProvider, provider),
                provider,
                serviceProvider.GetService<TimeProvider>()));

        return true;
    }

    /// <summary>Registers the selector. Call once, alongside the providers.</summary>
    public static IServiceCollection AddNoticeExtraction(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INoticeExtractorSelector, NoticeExtractorSelector>();

        return services;
    }
}
