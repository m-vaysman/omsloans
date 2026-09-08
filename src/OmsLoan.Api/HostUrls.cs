namespace OmsLoan.Api;

/// <summary>
/// The addresses Kestrel will actually listen on, read from configuration before the server
/// starts.
/// </summary>
/// <remarks>
/// There are two ways to tell Kestrel where to listen and they are not alternatives to each
/// other so much as different levels of detail: the flat <c>Urls</c> key (what
/// <c>ASPNETCORE_URLS</c> becomes, and what the installer sets), and the
/// <c>Kestrel:Endpoints</c> section, which is what you need once an endpoint has a
/// certificate or a protocol override. Configuration can supply either or both, so anything
/// reporting on or reasoning about the listening addresses has to look at both.
///
/// Reading them here rather than from <c>app.Urls</c> is deliberate: that collection is only
/// populated once the server is starting, which is after the point where the banner is
/// written and after the point where the pipeline has been built.
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
    /// Whether anything is listening over TLS. Drives the HTTPS redirection decision:
    /// <c>UseHttpsRedirection</c> on a host with no https endpoint cannot work out a port to
    /// redirect to, so it logs a warning on every request and then does nothing — noise that
    /// obscures the real reason a reviewer cannot reach the site.
    /// </summary>
    public static bool HasHttpsEndpoint(IConfiguration configuration) =>
        Configured(configuration).Any(url =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
