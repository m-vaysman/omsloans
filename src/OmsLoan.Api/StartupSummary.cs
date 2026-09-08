using System.Text;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace OmsLoan.Api;

/// <summary>
/// The banner written once at startup, naming the environment and where each setting was
/// resolved from.
/// </summary>
/// <remarks>
/// A service has no window and no console. When it starts against the wrong database or
/// with a stale key, the cause is almost always that configuration resolved from a
/// different source than whoever deployed it assumed — a leftover machine environment
/// variable outranking appsettings, or Production never being selected because
/// ASPNETCORE_ENVIRONMENT was not set on the service. Naming the winning source for each
/// setting turns that from an afternoon of guessing into the first line of the log.
///
/// The Api adds two lines the Worker has no use for. The listening addresses, because a
/// service that starts perfectly on Kestrel's default of <c>http://localhost:5000</c> is
/// indistinguishable in every other log line from one nobody else on the network can reach.
/// And whether a built SPA was found, because an API-only deployment and a broken web build
/// produce the same symptom in a browser — a blank page — and only one of them is a
/// deployment mistake.
///
/// Sources and presence only. Values are never written.
/// </remarks>
public static class StartupSummary
{
    public static void Log(
        ILogger logger,
        IWebHostEnvironment environment,
        IConfigurationRoot configuration)
    {
        var banner = new StringBuilder();
        banner.AppendLine("OmsLoan review API starting.");
        banner.AppendLine($"  Service          : {ServiceMetadata.ServiceName} ({ServiceMetadata.DisplayName})");
        banner.AppendLine($"  Environment      : {environment.EnvironmentName}");
        banner.AppendLine($"  Content root     : {environment.ContentRootPath}");
        banner.AppendLine($"  Web root         : {environment.WebRootPath ?? "(none)"}");
        banner.AppendLine($"  Running as       : {(WindowsServiceHelpers.IsWindowsService() ? "Windows Service" : "console")}");
        banner.AppendLine($"  Listening on     : {DescribeUrls(configuration)}");
        banner.AppendLine($"  React UI         : {DescribeSpa(environment)}");

        banner.AppendLine("  Configuration sources, lowest precedence first:");
        foreach (var provider in configuration.Providers)
        {
            banner.AppendLine($"    - {provider}");
        }

        banner.AppendLine("  Resolved settings:");
        banner.AppendLine($"    - Connection string : {Describe(configuration, ConfigurationKeys.ConnectionStringKey)}");

        logger.LogInformation("{StartupBanner}", banner.ToString().TrimEnd());

        WarnAboutMissingConfiguration(logger, environment, configuration);
    }

    private static string DescribeUrls(IConfigurationRoot configuration)
    {
        var urls = HostUrls.Configured(configuration);
        return urls.Count == 0
            ? "not configured — Kestrel default (http://localhost:5000), reachable only from this machine"
            : string.Join(", ", urls);
    }

    private static string DescribeSpa(IWebHostEnvironment environment)
    {
        var indexPath = SpaHosting.IndexFilePath(environment);
        return indexPath is null
            ? "not deployed — API only, no static files served"
            : $"served from {Path.GetDirectoryName(indexPath)}";
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
    /// Warnings rather than a refusal to start. A review API with no database is still a
    /// process the SCM reports as Running and whose log can be read; one that exits on
    /// startup gets restarted three times, gives up, and leaves the reason in whichever log
    /// nobody thought to open.
    /// </summary>
    private static void WarnAboutMissingConfiguration(
        ILogger logger,
        IWebHostEnvironment environment,
        IConfigurationRoot configuration)
    {
        if (WinningSource(configuration, ConfigurationKeys.ConnectionStringKey) is null)
        {
            logger.LogWarning(
                "No connection string found at {Key}. Set the {Variable} environment variable, "
                + "or add it to user-secrets in Development. See docs/api-windows-service.md.",
                ConfigurationKeys.ConnectionStringKey,
                ConfigurationKeys.ToEnvironmentVariable(ConfigurationKeys.ConnectionStringKey));
        }

        if (HostUrls.Configured(configuration).Count == 0 && !environment.IsDevelopment())
        {
            logger.LogWarning(
                "No listening address configured. Kestrel will bind http://localhost:5000, which "
                + "no other machine can reach. Set ASPNETCORE_URLS on the service — see "
                + "docs/api-windows-service.md.");
        }

        if (!SpaHosting.IsPresent(environment) && !environment.IsDevelopment())
        {
            logger.LogWarning(
                "No index.html under the web root, so the React review UI is not being served by "
                + "this process. Publish with the OmsLoan.Web build included, or point reviewers "
                + "at wherever it is hosted instead.");
        }
    }
}
