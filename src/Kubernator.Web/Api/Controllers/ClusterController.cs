using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Discovery;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Produces("application/json")]
[Tags("Cluster Provisioning")]
public sealed class ClusterController : ControllerBase
{
    private readonly IJobManager jobs;
    private readonly ClusterTopologyDiscoverer discoverer;
    private readonly ILogger<ClusterController> logger;

    public ClusterController(IJobManager jobs, ClusterTopologyDiscoverer discoverer, ILogger<ClusterController> logger)
    {
        this.jobs = jobs;
        this.discoverer = discoverer;
        this.logger = logger;
    }

    [HttpPost("pull")]
    [Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    public ActionResult<JobAcceptedResponse> Pull([FromBody] ClusterPullRequest request)
    {
        var output = ApiPathHelpers.RequireField(request?.OutputDirectory, "outputDirectory");
        var version = ApiPathHelpers.RequireField(request!.Version, "version");
        if (request.Architectures is not { Count: > 0 })
        {
            throw ApiException.BadRequest("architectures is required");
        }
        if (!ClusterDistroParsing.TryParse(request.Distro, out var distro))
        {
            throw ApiException.BadRequest($"unsupported distro: {request.Distro}");
        }
        if (distro is not (DistroKind.Rke2 or DistroKind.K3s or DistroKind.KubeadmNative))
        {
            throw ApiException.BadRequest($"pulling artifacts for distro '{distro}' is not implemented yet — only 'rke2', 'k3s', and 'kubeadm' are supported");
        }

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request with { OutputDirectory = Path.GetFullPath(output), Version = version };

        var record = jobs.Enqueue("cluster-pull", captured, keyId, keyName);
        logger.LogInformation("cluster pull job {Id} submitted by key={KeyId}", record.Id, keyId);
        return Accepted($"/api/v1/jobs/{record.Id}", JobAccepted(record));
    }

    [HttpPost("trust-host")]
    [Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
    [ProducesResponseType(typeof(ClusterTrustHostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClusterTrustHostResponse>> TrustHost([FromBody] ClusterTrustHostRequest request, CancellationToken ct)
    {
        var host = ApiPathHelpers.RequireField(request?.Host, "host");
        var username = ApiPathHelpers.RequireField(request!.Username, "username");

        var connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = host, Port = request.Port, Username = username };
        var fingerprint = await SshNodeExecutor.ProbeHostKeyFingerprintAsync(connection, ct);

        if (request.Confirm)
        {
            KnownHostsStore.Default().Trust(host, request.Port, fingerprint);
        }

        logger.LogInformation("cluster trust-host host={Host} port={Port} trusted={Trusted}", host, request.Port, request.Confirm);
        return Ok(new ClusterTrustHostResponse { Host = host, Port = request.Port, Fingerprint = fingerprint, Trusted = request.Confirm });
    }

    [HttpPost("install")]
    [Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobAcceptedResponse>> Install([FromBody] ClusterTopologyRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingFile(request?.TopologyPath, "topologyPath");
        var topology = await LoadTopologyAsync(path, ct);
        var captured = request! with { TopologyPath = path };

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;

        var record = jobs.Enqueue("cluster-install", captured, keyId, keyName);
        logger.LogInformation("cluster install job {Id} submitted by key={KeyId} cluster={Cluster}", record.Id, keyId, topology.ClusterName);
        return Accepted($"/api/v1/jobs/{record.Id}", JobAccepted(record));
    }

    [HttpPost("upgrade")]
    [Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobAcceptedResponse>> Upgrade([FromBody] ClusterUpgradeRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingFile(request?.TopologyPath, "topologyPath");
        var topology = await LoadTopologyAsync(path, ct);
        var toVersion = ApiPathHelpers.RequireField(request!.ToVersion, "toVersion");
        var captured = request with { TopologyPath = path };

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;

        var record = jobs.Enqueue("cluster-upgrade", captured, keyId, keyName);
        logger.LogInformation("cluster upgrade job {Id} submitted by key={KeyId} cluster={Cluster} to={Version}", record.Id, keyId, topology.ClusterName, toVersion);
        return Accepted($"/api/v1/jobs/{record.Id}", JobAccepted(record));
    }

    [HttpPost("status")]
    [Authorize(Policy = ApiKeyScopes.ReadPolicy)]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobAcceptedResponse>> Status([FromBody] ClusterStatusRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingFile(request?.TopologyPath, "topologyPath");
        var topology = await LoadTopologyAsync(path, ct);
        var captured = request! with { TopologyPath = path };

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;

        var record = jobs.Enqueue("cluster-status", captured, keyId, keyName);
        logger.LogInformation("cluster status job {Id} submitted by key={KeyId} cluster={Cluster}", record.Id, keyId, topology.ClusterName);
        return Accepted($"/api/v1/jobs/{record.Id}", JobAccepted(record));
    }

    [HttpPost("discover")]
    [Authorize(Policy = ApiKeyScopes.ReadPolicy)]
    [ProducesResponseType(typeof(ClusterDiscoverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClusterDiscoverResponse>> Discover([FromBody] ClusterDiscoverRequest request, CancellationToken ct)
    {
        var clusterName = ApiPathHelpers.RequireField(request?.ClusterName, "clusterName");
        var version = ApiPathHelpers.RequireField(request!.Version, "version");
        var username = ApiPathHelpers.RequireField(request.SshUsername, "sshUsername");
        if (!ClusterDistroParsing.TryParse(request.Distro, out var distro))
        {
            throw ApiException.BadRequest($"unsupported distro: {request.Distro}");
        }

        ClusterDiscoveryResult result;
        try
        {
            result = await discoverer.DiscoverAsync(new ClusterDiscoveryOptions
            {
                Context = string.IsNullOrWhiteSpace(request.Context) ? null : request.Context,
                ClusterName = clusterName,
                Distro = distro,
                Version = version,
                LocalArtifactBundlePath = string.IsNullOrWhiteSpace(request.LocalArtifactBundlePath)
                    ? "REPLACE_WITH_LOCAL_ARTIFACT_BUNDLE_PATH"
                    : request.LocalArtifactBundlePath,
                SshUsername = username,
                SshPrivateKeyVaultId = request.SshPrivateKeyVaultId,
                SshPrivateKeyPath = request.SshPrivateKeyPath,
                SshPort = request.SshPort,
                FixedRegistrationAddresses = request.FixedRegistrationAddresses
            }, ct);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            throw ApiException.BadRequest("cluster discovery failed", ex.Message);
        }

        var topologyJson = ClusterTopologyJson.Serialize(result.Topology);
        string? writtenTo = null;
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            writtenTo = Path.GetFullPath(request.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(writtenTo)!);
            await System.IO.File.WriteAllTextAsync(writtenTo, topologyJson, ct);
        }

        logger.LogInformation("cluster discover cluster={Cluster} nodes={Count} ok={Ok}", clusterName, result.Topology.Nodes.Count, result.Errors.Count == 0);
        return Ok(new ClusterDiscoverResponse
        {
            ClusterName = clusterName,
            TopologyOk = result.Errors.Count == 0,
            Errors = result.Errors,
            Warnings = result.Warnings,
            Nodes = result.Topology.Nodes.Select(n => new ClusterDiscoverNodeDto
            {
                Name = n.Name,
                Role = n.Role.ToString(),
                Host = n.Connection.Host,
                IsInitServer = n.IsInitServer
            }).ToArray(),
            TopologyJson = topologyJson,
            WrittenTo = writtenTo
        });
    }

    private static JobAcceptedResponse JobAccepted(JobRecord record) => new()
    {
        Id = record.Id,
        Kind = record.Kind,
        Status = record.Status.ToString(),
        Location = $"/api/v1/jobs/{record.Id}"
    };

    private static async Task<ClusterTopology> LoadTopologyAsync(string resolvedPath, CancellationToken ct)
    {
        try
        {
            return await ClusterTopologyJson.LoadAsync(resolvedPath, ct);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            throw ApiException.BadRequest("invalid topology file", ex.Message);
        }
    }
}
