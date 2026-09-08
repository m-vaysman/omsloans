using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace OmsLoan.Api;

/// <summary>
/// Serving the React review UI out of this same process in Production.
/// </summary>
/// <remarks>
/// One process serving both halves is the point. The alternative — Kestrel for the API and
/// something else for the static files — buys a second thing to install, a second thing to
/// recover after a reboot, a cross-origin story to configure correctly, and an extra hop for
/// the reviewer. None of that is worth it for a build output measured in hundreds of
/// kilobytes.
///
/// Development does not use any of this. Vite runs on its own port with hot module reload
/// and proxies API calls; <c>dotnet run</c> here serves nothing but the API. That asymmetry
/// is intentional and is why <see cref="IsPresent"/> exists rather than the pipeline simply
/// assuming a build is there.
///
/// The publish-time copy of <c>src/OmsLoan.Web/dist</c> into <c>wwwroot</c> is done by a
/// target in OmsLoan.Api.csproj.
/// </remarks>
internal static class SpaHosting
{
    /// <summary>
    /// Path prefix that belongs to the API and must never be answered with the SPA shell.
    /// </summary>
    public const string ApiPathPrefix = "/api";

    /// <summary>
    /// Whether a built SPA is actually sitting in the web root. Checked rather than assumed
    /// so that an API-only deployment, and every <c>dotnet run</c>, skips the static file
    /// middleware entirely instead of registering a fallback that can only ever 404.
    /// </summary>
    public static bool IsPresent(IWebHostEnvironment environment) =>
        IndexFilePath(environment) is not null;

    /// <summary>The absolute path of the SPA entry document, or null if there is not one.</summary>
    public static string? IndexFilePath(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            // Null when wwwroot does not exist under the content root at all — which is the
            // normal state of a developer checkout, since dist/ is gitignored.
            return null;
        }

        var indexPath = Path.Combine(webRoot, "index.html");
        return File.Exists(indexPath) ? indexPath : null;
    }

    /// <summary>
    /// Static file middleware for the built SPA. Must run before routing so a request for a
    /// hashed asset never touches the endpoint pipeline.
    /// </summary>
    public static WebApplication UseOmsLoanSpa(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(StaticFileOptions());
        return app;
    }

    /// <summary>
    /// The catch-all that turns a deep link such as <c>/notices/42</c> — a client-side route
    /// the server knows nothing about — into the SPA shell, so a refresh or a pasted URL
    /// lands where the reviewer expects instead of on a 404.
    /// </summary>
    /// <remarks>
    /// Registered after <c>MapControllers</c> and, more importantly, registered as a
    /// fallback: fallback endpoints sort behind every real endpoint, so a controller route
    /// always wins and Swagger keeps its own paths.
    ///
    /// The explicit <c>/api</c> fallback is the part that is easy to leave out and painful to
    /// debug. Without it, a request to a misspelled or not-yet-implemented API route matches
    /// the SPA catch-all and comes back as <c>200 text/html</c>. The caller then fails
    /// deserialising an HTML document, a long way from the cause. Returning 404 for anything
    /// under <c>/api</c> that matched no controller keeps the failure where it happened.
    /// </remarks>
    public static WebApplication MapOmsLoanSpaFallback(this WebApplication app)
    {
        app.MapFallback($"{ApiPathPrefix}/{{**path}}", () => Results.NotFound());
        app.MapFallbackToFile("index.html", StaticFileOptions());
        return app;
    }

    /// <summary>
    /// Cache policy for the build output, and the reason a deploy is visible on the next
    /// refresh rather than whenever a browser happens to give up on what it has.
    /// </summary>
    /// <remarks>
    /// Vite emits every asset with a content hash in its filename, so <c>assets/</c> is
    /// immutable by construction and can be cached for a year. <c>index.html</c> is the one
    /// file whose name never changes and whose contents name the current hashed bundles, so
    /// it must not be cached at all — a stale copy points at bundles that a deploy has
    /// already deleted, and the application fails to boot with nothing in any server log.
    /// </remarks>
    private static StaticFileOptions StaticFileOptions() => new()
    {
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.GetTypedHeaders();
            var name = context.File.Name;

            if (string.Equals(name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            }
            else
            {
                headers.CacheControl = new CacheControlHeaderValue
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(365),
                };
            }
        },

        // The default provider does not know about every extension a build can emit, and an
        // unknown type is not served at all rather than served with a guess. Vite fonts are
        // the usual casualty.
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
    };
}
