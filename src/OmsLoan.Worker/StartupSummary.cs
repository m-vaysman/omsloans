using System.Text;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace OmsLoan.Worker;

/// <summary>
/// The banner written once at startup, naming the environment and where each setting was
/// resolved from.
/// </summary>
/// <remarks>
/// A service has no window and no console. When it starts against the wrong database or
/// with a stale key, the cause is almost always that configuration resolved from a
/// different source than whoever deployed it assumed — a leftover machine environment
/// variable outranking appsettings, or Production never being selected because
/// DOTNET_ENVIRONMENT was not set on the service. Naming the winning source for each
/// setting turns that from an afternoon of guessing into the first line of the log.
///
/// For the flat-named secrets the banner goes one better and names the variable itself, so a
/// missing key reads as "set GROQ_API_KEY" rather than leaving the reader to work out which
/// of two spellings the Worker wanted.
///
/// Sources and presence only. Values are never written, and the secret checks report
/// nothing beyond whether something was found.
/// </remarks>
public static class StartupSummary
{
    public static void Log(ILogger logger, IHostEnvironment environment, IConfigurationRoot configuration)
    {
        var banner = new StringBuilder();
        banner.AppendLine("OmsLoan worker starting.");
        banner.AppendLine($"  Service          : {ServiceMetadata.ServiceName} ({ServiceMetadata.DisplayName})");
        banner.AppendLine($"  Environment      : {environment.EnvironmentName}");
        banner.AppendLine($"  Content root     : {environment.ContentRootPath}");
        banner.AppendLine($"  Running as       : {(WindowsServiceHelpers.IsWindowsService() ? "Windows Service" : "console")}");

        banner.AppendLine("  Configuration sources, lowest precedence first:");
        foreach (var provider in configuration.Providers)
        {
            banner.AppendLine($"    - {provider}");
        }

        banner.AppendLine("  Resolved settings:");

        var width = ConfigurationKeys.AllSecrets
            .Select(secret => secret.ConfigurationKey.Length)
            .Append(ConfigurationKeys.ConnectionStringKey.Length)
            .Max();

        banner.Append($"    - {ConfigurationKeys.ConnectionStringKey.PadRight(width)} : ")
              .AppendLine(Describe(configuration, ConfigurationKeys.ConnectionStringKey));

        foreach (var secret in ConfigurationKeys.AllSecrets)
        {
            banner.Append($"    - {secret.ConfigurationKey.PadRight(width)} : ")
                  .AppendLine(DescribeSecret(configuration, secret));
        }

        logger.LogInformation("{StartupBanner}", banner.ToString().TrimEnd());

        WarnAboutMissingSecrets(logger, environment, configuration);
    }

    /// <summary>
    /// Presence and winning source for one key. Never the value: this line goes to the
    /// Event Log and to any log file the service writes.
    /// </summary>
    private static string Describe(IConfigurationRoot configuration, string key)
    {
        var source = WinningSource(configuration, key);
        return source is null
            ? "absent"
            : $"present (from {source})";
    }

    /// <summary>
    /// As <see cref="Describe"/>, but reports the flat variable name rather than the provider
    /// — both when the value came from there and, more usefully, when it is missing and the
    /// reader needs to know what to set.
    /// </summary>
    private static string DescribeSecret(IConfigurationRoot configuration, SecretSetting secret)
    {
        var source = WinningSource(configuration, secret.ConfigurationKey);

        if (source is null)
        {
            return $"absent (set {secret.EnvironmentVariable})";
        }

        return source.StartsWith("Flat environment secrets", StringComparison.Ordinal)
            ? $"present (from {secret.EnvironmentVariable})"
            : $"present (from {source})";
    }

    /// <summary>
    /// The last provider that supplies a key wins in .NET configuration, so the search runs
    /// in reverse. Returning the provider rather than a boolean is the point of the banner.
    /// </summary>
    private static string? WinningSource(IConfigurationRoot configuration, string key)
    {
        foreach (var provider in configuration.Providers.Reverse())
        {
            if (provider.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return provider.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// A missing key is a warning rather than a startup failure: the extractor issues have
    /// not landed yet, and a Worker that refuses to start because one of three optional
    /// providers is unconfigured would be worse than one that says so and carries on.
    /// </summary>
    private static void WarnAboutMissingSecrets(
        ILogger logger,
        IHostEnvironment environment,
        IConfigurationRoot configuration)
    {
        // The connection string and the Graph credential are not warned about here: they are
        // required, and StartupValidation refuses to start without them. Warning and then
        // failing about the same setting would only obscure which of the two mattered.
        var missingProviders = Missing(configuration, ConfigurationKeys.ProviderApiKeys);

        if (missingProviders.Count == ConfigurationKeys.ProviderApiKeys.Count)
        {
            logger.LogWarning(
                "No extraction provider API keys are configured. Notices will be ingested but "
                + "not extracted. Set {Variables} as machine environment variables, or in {Mechanism} "
                + "for {Environment}. See docs/windows-service.md.",
                Join(missingProviders),
                environment.IsDevelopment() ? "dotnet user-secrets" : "the service environment block",
                environment.EnvironmentName);
        }
        else if (missingProviders.Count > 0)
        {
            logger.LogInformation(
                "Extraction providers without a configured key: {Variables}. Those providers are disabled.",
                Join(missingProviders));
        }

    }

    private static List<SecretSetting> Missing(
        IConfigurationRoot configuration,
        IReadOnlyList<SecretSetting> secrets) =>
        [.. secrets.Where(secret => WinningSource(configuration, secret.ConfigurationKey) is null)];

    private static string Join(IEnumerable<SecretSetting> secrets) =>
        string.Join(", ", secrets.Select(secret => secret.EnvironmentVariable));
}
