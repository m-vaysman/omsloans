using System.Text;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace OmsLoan.Api;

/// <summary>
/// Banner at startup: environment and where each setting resolved from.
/// </summary>
/// <remarks>
/// An installed Windows service has no window and no console. Wrong database or stale key
/// almost always means configuration won from a different source than assumed — leftover
/// machine env outranking appsettings, or Production never selected because
/// ASPNETCORE_ENVIRONMENT was unset on the service. Naming the winning source turns guessing
/// into the first log line.
///
/// The Api adds two lines the Worker does not need: listening addresses (Kestrel's
/// <c>http://localhost:5000</c> default looks healthy and is unreachable from other machines),
/// and whether a built SPA was found (API-only and a broken web build both show a blank page).
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
    /// process Windows Service Control Manager reports as Running whose log can be read; one
    /// that exits on startup gets restarted three times and leaves the reason in a log nobody
    /// opened.
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
