using System.Text;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace OmsLoan.Worker;

/// <summary>
/// Banner at startup: environment and where each setting resolved from.
/// </summary>
/// <remarks>
/// An installed Windows service has no window and no console. Wrong database or stale key
/// almost always means configuration won from a different source than assumed — leftover
/// machine env outranking appsettings, or Production never selected because
/// DOTNET_ENVIRONMENT was unset on the service. Naming the winning source turns an afternoon
/// of guessing into the first log line.
///
/// For flat secrets the banner names the variable itself, so a miss reads "set GROQ_API_KEY"
/// rather than forcing the reader to pick between two spellings.
///
/// Sources and presence only. Values are never written.
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
            .Append(ConfigurationKeys.WatchedFolder.ConfigurationKey.Length)
            .Max();

        banner.Append($"    - {ConfigurationKeys.ConnectionStringKey.PadRight(width)} : ")
              .AppendLine(Describe(configuration, ConfigurationKeys.ConnectionStringKey));

        // Watched folder is safe to print and most worth printing: watching the wrong path
        // looks identical from every other line in this log.
        var watchedFolder = configuration[ConfigurationKeys.WatchedFolder.ConfigurationKey];
        banner.Append($"    - {ConfigurationKeys.WatchedFolder.ConfigurationKey.PadRight(width)} : ")
              .AppendLine(string.IsNullOrWhiteSpace(watchedFolder)
                  ? $"absent (set {ConfigurationKeys.WatchedFolder.EnvironmentVariable})"
                  : watchedFolder);

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
    private static string DescribeSecret(IConfigurationRoot configuration, ConfiguredSetting secret)
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
    /// A missing provider key is a warning, not a startup failure: providers are individually
    /// optional — any one will do — and refusing to start because one of three is unconfigured
    /// would be worse than saying so and carrying on.
    /// </summary>
    private static void WarnAboutMissingSecrets(
        ILogger logger,
        IHostEnvironment environment,
        IConfigurationRoot configuration)
    {
        // Connection string and Graph are not warned here: StartupValidation refuses without
        // them. Warning then failing on the same setting would obscure which mattered.
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

    private static List<ConfiguredSetting> Missing(
        IConfigurationRoot configuration,
        IReadOnlyList<ConfiguredSetting> secrets) =>
        [.. secrets.Where(secret => WinningSource(configuration, secret.ConfigurationKey) is null)];

    private static string Join(IEnumerable<ConfiguredSetting> secrets) =>
        string.Join(", ", secrets.Select(secret => secret.EnvironmentVariable));
}
