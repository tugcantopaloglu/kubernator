using Kubernator.Web.Api;
using Kubernator.Web.Services;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class BuildJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<BuildRequest>
{
    public override string Kind => "build";

    protected override async Task<object?> RunAsync(BuildRequest payload, JobContext ctx, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<BuildPipeline>();
        ctx.Report($"build {payload.Path} → {payload.OutputDirectory ?? "<auto>"}");
        var result = await pipeline.RunAsync(new BuildPipelineRequest
        {
            Path = payload.Path,
            OutputDirectory = payload.OutputDirectory,
            ImageName = payload.ImageName,
            ImageTag = payload.ImageTag,
            NoBuild = payload.NoBuild,
            Platforms = payload.Platforms
        }, ctx.AsProgress(), ct);
        return BuildResultDto.From(result);
    }
}
