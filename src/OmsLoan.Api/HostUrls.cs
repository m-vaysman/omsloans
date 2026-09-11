namespace OmsLoan.Api;

/// <summary>
/// Addresses Kestrel will listen on, read from configuration before the server starts.
/// </summary>
/// <remarks>
/// Two levels of detail, not alternatives: flat <c>Urls</c> (what <c>ASPNETCORE_URLS</c>
/// becomes, what the installer sets), and <c>Kestrel:Endpoints</c> when a certificate or
/// protocol override is needed. Either or both may be set, so reporting must look at both.
///
/// Read here rather than from <c>app.Urls</c>: that collection is only populated once the
/// server is starting — after the banner and after the pipeline is built.
/// </remarks>
internal static class HostUrls
{
    /// <summary>
    /// Every configured listening address, in no particular order. Empty means nothing was
    /// configured and Kestrel will fall back to its own default of
    /// <c>http://localhost:5000</c> — which is a working service that no other machine can
    /// reach, so the banner calls it out.
    /// </summary>
    public static IReadOnlyList<string> Configured(IConfiguration configuration)
    {
        var urls = new List<string>();

        var flat = configuration[ConfigurationKeys.UrlsKey];
        if (!string.IsNullOrWhiteSpace(flat))
        {
            urls.AddRange(flat.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var endpoint in configuration.GetSection(ConfigurationKeys.KestrelEndpointsSection).GetChildren())
        {
            var url = endpoint["Url"];
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Whether anything listens over TLS. Drives HTTPS redirection: without an https endpoint
    /// the middleware cannot pick a port, warns on every request, and does nothing — noise that
    /// hides why a reviewer cannot reach the site.
    /// </summary>
    public static bool HasHttpsEndpoint(IConfiguration configuration) =>
        Configured(configuration).Any(url =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
