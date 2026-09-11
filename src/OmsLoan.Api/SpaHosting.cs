using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace OmsLoan.Api;

/// <summary>
/// Serve the React review UI from this same process in Production.
/// </summary>
/// <remarks>
/// One process for both halves is the point. A separate static host buys a second install, a
/// second recovery after reboot, CORS to get right, and an extra hop — not worth it for a
/// few hundred kilobytes of build output.
///
/// Development skips this. Vite runs on its own port with HMR and proxies the API; Visual
/// Studio / <c>dotnet run</c> serve the API only. That asymmetry is why <see cref="IsPresent"/>
/// exists rather than assuming a build is there.
///
/// Publish copies <c>src/OmsLoan.Web/dist</c> into <c>wwwroot</c> via OmsLoan.Api.csproj.
/// </remarks>
internal static class SpaHosting
{
    /// <summary>
    /// Path prefix that belongs to the API and must never be answered with the SPA shell.
    /// </summary>
    public const string ApiPathPrefix = "/api";

    /// <summary>
    /// Whether a built SPA sits in the web root. Checked so API-only deploys and every Visual
    /// Studio / <c>dotnet run</c> skip static middleware instead of registering a fallback that
    /// can only 404.
    /// </summary>
    public static bool IsPresent(IWebHostEnvironment environment) =>
        IndexFilePath(environment) is not null;

    /// <summary>The absolute path of the SPA entry document, or null if there is not one.</summary>
    public static string? IndexFilePath(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            // Null when wwwroot is missing under the content root — normal for a developer
            // checkout; dist/ is gitignored.
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
    /// Catch-all for client routes such as <c>/notices/42</c>: refresh or a pasted URL gets
    /// the SPA shell instead of a 404.
    /// </summary>
    /// <remarks>
    /// Registered after <c>MapControllers</c> as a fallback so every real endpoint wins and
    /// Swagger keeps its paths.
    ///
    /// The explicit <c>/api</c> fallback is easy to omit and painful to debug. Without it a
    /// misspelled API route matches the SPA catch-all and returns <c>200 text/html</c> — the
    /// caller fails deserialising HTML, far from the cause. 404 under <c>/api</c> keeps the
    /// failure where it happened.
    /// </remarks>
    public static WebApplication MapOmsLoanSpaFallback(this WebApplication app)
    {
        app.MapFallback($"{ApiPathPrefix}/{{**path}}", () => Results.NotFound());
        app.MapFallbackToFile("index.html", StaticFileOptions());
        return app;
    }

    /// <summary>
    /// Cache policy so a deploy is visible on the next refresh, not whenever the browser gives
    /// up on what it cached.
    /// </summary>
    /// <remarks>
    /// Vite hashes asset filenames, so <c>assets/</c> is immutable and can cache for a year.
    /// <c>index.html</c> never changes name and names the current bundles — it must not be
    /// cached. A stale copy points at deleted bundles; the app fails to boot with nothing in
    /// any server log.
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

        // Default provider misses some build extensions; unknown types are not served at all.
        // Vite fonts are the usual casualty.
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
    };
}
