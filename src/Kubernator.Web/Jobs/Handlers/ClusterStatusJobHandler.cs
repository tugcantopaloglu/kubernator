using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class ClusterStatusJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<ClusterStatusRequest>
{
    public override string Kind => "cluster-status";

    protected override async Task<object?> RunAsync(ClusterStatusRequest payload, JobContext ctx, CancellationToken ct)
    {
        var topology = await ClusterTopologyJson.LoadAsync(payload.TopologyPath, ct);

        using var scope = scopeFactory.CreateScope();
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

        var plan = await planner.PlanAsync(topology, topology.Version, ct);
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
}
