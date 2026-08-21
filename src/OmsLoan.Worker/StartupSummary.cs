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
        banner.AppendLine($"    - Connection string : {Describe(configuration, ConfigurationKeys.ConnectionStringKey)}");
        foreach (var key in ConfigurationKeys.ProviderApiKeys)
        {
            banner.Append($"    - {key,-26}: ").AppendLine(Describe(configuration, key));
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
        if (WinningSource(configuration, ConfigurationKeys.ConnectionStringKey) is null)
        {
            logger.LogWarning(
                "No connection string found at {Key}. Set the {Variable} environment variable, "
                + "or add it to user-secrets in Development. See docs/windows-service.md.",
                ConfigurationKeys.ConnectionStringKey,
                ConfigurationKeys.ToEnvironmentVariable(ConfigurationKeys.ConnectionStringKey));
        }

        var missing = ConfigurationKeys.ProviderApiKeys
            .Where(key => WinningSource(configuration, key) is null)
            .ToList();

        if (missing.Count == ConfigurationKeys.ProviderApiKeys.Count)
        {
            logger.LogWarning(
                "No extraction provider API keys are configured. Notices will be ingested but "
                + "not extracted. In {Environment}, supply them via {Mechanism}.",
                environment.EnvironmentName,
                environment.IsDevelopment() ? "dotnet user-secrets" : "environment variables");
        }
        else if (missing.Count > 0)
        {
            logger.LogInformation(
                "Extraction providers without a configured key: {Missing}. Those providers are disabled.",
                string.Join(", ", missing));
        }
    }
}
