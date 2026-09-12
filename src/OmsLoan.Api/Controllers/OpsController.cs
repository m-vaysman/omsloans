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
    public ContentResult Page() => Content(OpsPage.Html, "text/html; charset=utf-8");

    [HttpGet(OpsRoutes.Status)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OpsStatus> Status(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        return await OpsStatusFactory.BuildAsync(
            _ops,
            configuration,
            StubOpsDatabaseProbe.Instance,
            DateTimeOffset.UtcNow,
            stub: true,
            cancellationToken);
    }
}
