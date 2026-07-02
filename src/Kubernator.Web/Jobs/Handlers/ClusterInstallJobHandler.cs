using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class ClusterInstallJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<ClusterTopologyRequest>
{
    public override string Kind => "cluster-install";

    protected override async Task<object?> RunAsync(ClusterTopologyRequest payload, JobContext ctx, CancellationToken ct)
    {
        var topology = await ClusterTopologyJson.LoadAsync(payload.TopologyPath, ct);

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IClusterProvisioningService>();
        var result = await service.InstallAsync(
            new ClusterProvisionOptions { Topology = topology, AllowProduction = payload.AllowProduction }, ctx.AsProgress(), ct);
        return ClusterInstallResultDto.From(result);
    }
}
