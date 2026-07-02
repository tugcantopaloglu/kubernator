using Kubernator.Core.ClusterProvisioning.Artifacts;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class ClusterPullJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<ClusterPullRequest>
{
    public override string Kind => "cluster-pull";

    protected override async Task<object?> RunAsync(ClusterPullRequest payload, JobContext ctx, CancellationToken ct)
    {
        if (!ClusterDistroParsing.TryParse(payload.Distro, out var distro))
        {
            throw new InvalidOperationException($"unsupported distro: {payload.Distro}");
        }

        using var scope = scopeFactory.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IClusterArtifactBundleService>();
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = payload.OutputDirectory,
            Distro = distro,
            Version = payload.Version,
            Architectures = payload.Architectures,
            IncludeKubectl = payload.IncludeKubectl,
            IncludeHelm = payload.IncludeHelm,
            IncludeK9s = payload.IncludeK9s,
            HelmVersion = payload.HelmVersion ?? "v3.16.2",
            K9sVersion = payload.K9sVersion ?? "v0.32.5",
            IncludeSelinuxPolicy = payload.IncludeSelinuxPolicy,
            SelinuxPolicyVersion = payload.SelinuxPolicyVersion
        };
        var manifest = await artifacts.PullAsync(options, ctx.AsProgress(), ct);

        string? packed = null;
        if (!string.IsNullOrWhiteSpace(payload.PackArchivePath))
        {
            packed = await artifacts.PackAsync(payload.OutputDirectory, Path.GetFullPath(payload.PackArchivePath), ctx.AsProgress(), ct);
        }
        return ClusterArtifactManifestDto.From(manifest, packed);
    }
}
