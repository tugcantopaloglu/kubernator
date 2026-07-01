using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Artifacts;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
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
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IJobManager jobs;
    private readonly ILogger<ClusterController> logger;

    public ClusterController(IServiceScopeFactory scopeFactory, IJobManager jobs, ILogger<ClusterController> logger)
    {
        this.scopeFactory = scopeFactory;
        this.jobs = jobs;
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
        if (!TryParseDistro(request.Distro, out var distro))
        {
            throw ApiException.BadRequest($"unsupported distro: {request.Distro}");
        }
        if (distro is not (DistroKind.Rke2 or DistroKind.K3s))
        {
            throw ApiException.BadRequest($"pulling artifacts for distro '{distro}' is not implemented yet — only 'rke2' and 'k3s' are supported");
        }

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request with { OutputDirectory = Path.GetFullPath(output), Version = version };
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "cluster-pull",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, ct) =>
            {
                using var scope = sf.CreateScope();
                var artifacts = scope.ServiceProvider.GetRequiredService<IClusterArtifactBundleService>();
                var options = new ClusterArtifactPullOptions
                {
                    OutputDirectory = captured.OutputDirectory,
                    Distro = distro,
                    Version = captured.Version,
                    Architectures = captured.Architectures,
                    IncludeKubectl = captured.IncludeKubectl,
                    IncludeHelm = captured.IncludeHelm,
                    IncludeK9s = captured.IncludeK9s,
                    HelmVersion = captured.HelmVersion ?? "v3.16.2",
                    K9sVersion = captured.K9sVersion ?? "v0.32.5",
                    IncludeSelinuxPolicy = captured.IncludeSelinuxPolicy,
                    SelinuxPolicyVersion = captured.SelinuxPolicyVersion
                };
                var manifest = await artifacts.PullAsync(options, ctx.AsProgress(), ct);

                string? packed = null;
                if (!string.IsNullOrWhiteSpace(captured.PackArchivePath))
                {
                    packed = await artifacts.PackAsync(captured.OutputDirectory, Path.GetFullPath(captured.PackArchivePath), ctx.AsProgress(), ct);
                }
                return ClusterArtifactManifestDto.From(manifest, packed);
            }
        });
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
        var topology = await LoadTopologyAsync(request?.TopologyPath, ct);
        var allowProduction = request!.AllowProduction;

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "cluster-install",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, jobCt) =>
            {
                using var scope = sf.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClusterProvisioningService>();
                var result = await service.InstallAsync(
                    new ClusterProvisionOptions { Topology = topology, AllowProduction = allowProduction }, ctx.AsProgress(), jobCt);
                return ClusterInstallResultDto.From(result);
            }
        });
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
        var topology = await LoadTopologyAsync(request?.TopologyPath, ct);
        var toVersion = ApiPathHelpers.RequireField(request!.ToVersion, "toVersion");
        var allowProduction = request.AllowProduction;

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "cluster-upgrade",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, jobCt) =>
            {
                using var scope = sf.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClusterProvisioningService>();
                var result = await service.UpgradeAsync(
                    new ClusterProvisionOptions { Topology = topology, AllowProduction = allowProduction }, toVersion, ctx.AsProgress(), jobCt);
                return ClusterUpgradeResultDto.From(result);
            }
        });
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
        var topology = await LoadTopologyAsync(request?.TopologyPath, ct);

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "cluster-status",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, jobCt) =>
            {
                using var scope = sf.CreateScope();
                var planner = scope.ServiceProvider.GetRequiredService<ClusterUpgradePlanner>();
                var validation = ClusterTopologyValidator.Validate(topology);
                foreach (var warning in validation.Warnings)
                {
                    ctx.Report($"warning: {warning}");
                }
                foreach (var error in validation.Errors)
                {
                    ctx.Report($"error: {error}");
                }

                var plan = await planner.PlanAsync(topology, topology.Version, jobCt);
                return new ClusterStatusResponse
                {
                    ClusterName = topology.ClusterName,
                    TopologyOk = validation.Ok,
                    Errors = validation.Errors,
                    Warnings = validation.Warnings,
                    Nodes = plan.Steps.Select(s => new ClusterNodeStatusDto
                    {
                        Name = s.Node.Name,
                        Role = s.Node.Role.ToString(),
                        Os = $"{s.Os.DistroId} {s.Os.VersionId} ({s.Os.Arch})",
                        CurrentVersion = s.CurrentVersion,
                        TargetVersion = s.TargetVersion,
                        NeedsUpgrade = s.NeedsUpgrade
                    }).ToArray()
                };
            }
        });
        logger.LogInformation("cluster status job {Id} submitted by key={KeyId} cluster={Cluster}", record.Id, keyId, topology.ClusterName);
        return Accepted($"/api/v1/jobs/{record.Id}", JobAccepted(record));
    }

    private static JobAcceptedResponse JobAccepted(JobRecord record) => new()
    {
        Id = record.Id,
        Kind = record.Kind,
        Status = record.Status.ToString(),
        Location = $"/api/v1/jobs/{record.Id}"
    };

    private static async Task<ClusterTopology> LoadTopologyAsync(string? path, CancellationToken ct)
    {
        var resolved = ApiPathHelpers.ResolveExistingFile(path, "topologyPath");
        try
        {
            return await ClusterTopologyJson.LoadAsync(resolved, ct);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            throw ApiException.BadRequest("invalid topology file", ex.Message);
        }
    }

    private static bool TryParseDistro(string raw, out DistroKind distro)
    {
        switch (raw.ToLowerInvariant())
        {
            case "rke2":
                distro = DistroKind.Rke2;
                return true;
            case "k3s":
                distro = DistroKind.K3s;
                return true;
            case "kubeadm":
            case "kubeadm-native":
                distro = DistroKind.KubeadmNative;
                return true;
            default:
                distro = default;
                return false;
        }
    }
}
