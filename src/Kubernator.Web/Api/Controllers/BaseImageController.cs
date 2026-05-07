using Kubernator.Core.Strategy;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/base-images")]
[Produces("application/json")]
[Tags("Inspection")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class BaseImageController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BaseImageInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<BaseImageInfoResponse> Get() => Ok(new BaseImageInfoResponse
    {
        AllowedRegistries = AllowedRegistries.All.OrderBy(x => x).ToArray()
    });
}
