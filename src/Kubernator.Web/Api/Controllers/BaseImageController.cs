using Kubernator.Core.Strategy;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/base-images")]
[Produces("application/json")]
[Tags("Inspection")]
public sealed class BaseImageController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BaseImageInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<BaseImageInfoResponse> Get() => Ok(new BaseImageInfoResponse
    {
        AllowedRegistries = AllowedRegistries.All.OrderBy(x => x).ToArray()
    });
}
