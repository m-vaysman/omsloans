using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmsLoan.Api.Ops;

namespace OmsLoan.Api.Controllers;

[ApiController]
public sealed class OpsController(IConfiguration configuration, IOptions<OpsOptions> opsOptions)
    : ControllerBase
{
    private readonly OpsOptions _ops = opsOptions.Value;

    [HttpGet(OpsRoutes.Page)]
    [ApiExplorerSettings(IgnoreApi = true)]
    // Controller action, not a file under wwwroot. Static files register only when
    // SpaHosting.IsPresent — an Api-only publish and every Visual Studio /
    // `dotnet run` checkout serve nothing from wwwroot.
    public ContentResult Page() => Content(OpsPage.Html, "text/html; charset=utf-8");

    [HttpGet(OpsRoutes.Status)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OpsStatus> Status(CancellationToken cancellationToken)
    {
        // no-store: a reviewer refresh must not show a cached poll.
        Response.Headers.CacheControl = "no-store";

        // Phase 1: stub: true until the real service and Event Log probes land.
        // The payload still reads real provider, endpoint, and secret presence.
        return await OpsStatusFactory.BuildAsync(
            _ops,
            configuration,
            StubOpsDatabaseProbe.Instance,
            DateTimeOffset.UtcNow,
            stub: true,
            cancellationToken);
    }
}
