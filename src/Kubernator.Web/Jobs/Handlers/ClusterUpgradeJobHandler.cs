using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class ClusterUpgradeJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<ClusterUpgradeRequest>
{
    public override string Kind => "cluster-upgrade";

    protected override async Task<object?> RunAsync(ClusterUpgradeRequest payload, JobContext ctx, CancellationToken ct)
    {
        var topology = await ClusterTopologyJson.LoadAsync(payload.TopologyPath, ct);

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IClusterProvisioningService>();
        var result = await service.UpgradeAsync(
            new ClusterProvisionOptions { Topology = topology, AllowProduction = payload.AllowProduction }, payload.ToVersion, ctx.AsProgress(), ct);
        return ClusterUpgradeResultDto.From(result);
    }
}
