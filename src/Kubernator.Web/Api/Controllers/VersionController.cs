using System.Runtime.InteropServices;
using Kubernator.Core.Updates;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/version")]
[Produces("application/json")]
[Tags("System")]
public sealed class VersionController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status200OK)]
    public ActionResult<VersionResponse> Get() => Ok(new VersionResponse
    {
        Version = KubernatorVersion.Current,
        Os = RuntimeInformation.OSDescription,
        Architecture = RuntimeInformation.OSArchitecture.ToString(),
        Framework = RuntimeInformation.FrameworkDescription
    });
}
