using Kubernator.Core.Monitoring;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/monitor")]
[Produces("application/json")]
[Tags("Deployment")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class MonitorController : ControllerBase
{
    private readonly IClusterMonitor monitor;
    private readonly ILogger<MonitorController> logger;

    public MonitorController(IClusterMonitor monitor, ILogger<MonitorController> logger)
    {
        this.monitor = monitor;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(MonitorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MonitorResponse>> Snapshot([FromBody] MonitorRequest request, CancellationToken ct)
    {
        ApiPathHelpers.RequireField(request?.Context, "context");
        logger.LogInformation("monitor snapshot context={Context} ns={Namespace}",
            request!.Context, request.Namespace ?? "<all>");

        var options = new ClusterMonitorOptions
        {
            Context = request.Context,
            Namespace = request.Namespace,
            IncludeMetrics = request.IncludeMetrics,
            IncludePods = request.IncludePods,
            IncludeIngress = request.IncludeIngress,
            IncludeNetworkPolicies = request.IncludeNetworkPolicies,
            IncludeServices = request.IncludeServices,
            KubectlBinary = request.KubectlBinary ?? "kubectl"
        };
        var snapshot = await monitor.GetSnapshotAsync(options, ct);
        return Ok(MonitorResponse.From(snapshot));
    }
}
