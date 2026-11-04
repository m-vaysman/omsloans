namespace OmsLoan.Api.Ops;

public static class OpsRoutes
{
    // Exact `/ops`, not a folder. UseDefaultFiles only rewrites paths ending in `/`.
    // Otherwise GET /ops hits the SPA catch-all and returns the React shell with 200.
    public const string Page = "/ops";

    // Under /api so a mistype inherits SpaHosting's `/api/{**path}` 404 instead of
    // the SPA catch-all returning HTML with 200.
    public const string Status = "/api/ops/status";

    public const int PollMilliseconds = 5000;
}
