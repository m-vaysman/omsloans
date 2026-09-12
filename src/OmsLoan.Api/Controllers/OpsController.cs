using Microsoft.AspNetCore.Mvc;
using OmsLoan.Api.Ops;

namespace OmsLoan.Api.Controllers;

[ApiController]
public sealed class OpsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet(OpsRoutes.Page)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ContentResult Page() => Content(OpsPage.Html, "text/html; charset=utf-8");

    [HttpGet(OpsRoutes.Status)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OpsStatus> Status(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        return await OpsStatusFactory.BuildAsync(
            configuration,
            StubOpsDatabaseProbe.Instance,
            DateTimeOffset.UtcNow,
            stub: true,
            cancellationToken);
    }
}
