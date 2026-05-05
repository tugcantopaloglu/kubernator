using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;

namespace Kubernator.Web.Services;

public sealed record BuildPipelineRequest
{
    public required string Path { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public bool NoBuild { get; init; }
    public IReadOnlyList<string>? Platforms { get; init; }
}

public sealed record BuildPipelineResult
{
    public required string OutputDirectory { get; init; }
    public required int GeneratedFileCount { get; init; }
    public string? ImageReference { get; init; }
    public long? ImageSizeBytes { get; init; }
    public string? EngineName { get; init; }
    public string? EngineVersion { get; init; }
}

public sealed class BuildPipeline
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;
    private readonly IContainerEngineProvider engineProvider;

    public BuildPipeline(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation,
        IContainerEngineProvider engineProvider)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
        this.engineProvider = engineProvider;
    }

    public async Task<BuildPipelineResult> RunAsync(
        BuildPipelineRequest request,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var path = Path.GetFullPath(request.Path);

        progress?.Report($"analyzing {path}");
        var descriptor = await analysis.AnalyzeAsync(path, ct);

        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag
        });

        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(path, ".kubernator")
            : Path.GetFullPath(request.OutputDirectory);

        progress?.Report($"generating into {output}");
        var genResult = await generation.GenerateAsync(plan, new GenerationOptions
        {
            OutputDirectory = output,
            OverwriteExisting = true
        }, ct);
        progress?.Report($"generated {genResult.WrittenFiles.Count} file(s)");

        if (request.NoBuild)
        {
            return new BuildPipelineResult
            {
                OutputDirectory = output,
                GeneratedFileCount = genResult.WrittenFiles.Count
            };
        }

        var requireMulti = request.Platforms is { Count: > 0 } && request.Platforms.Any(p => !string.IsNullOrWhiteSpace(p));
        var engine = await engineProvider.ResolveAsync(requireMulti, ct);
        var info = await engine.GetInfoAsync(ct);
        if (requireMulti && !engine.SupportsMultiPlatform)
        {
            throw new InvalidOperationException("the resolved container engine does not support multi-platform builds; install Docker Buildx or run a single-platform build");
        }
        progress?.Report($"engine {info.Name} {info.Version} ({info.OperatingSystem}/{info.Architecture})");

        var stagingDir = Path.Combine(output, "build-context");
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
        Directory.CreateDirectory(stagingDir);

        await CopyDirectoryAsync(path, stagingDir, output, ct);

        var dockerfileSrc = Path.Combine(output, "Dockerfile");
        File.Copy(dockerfileSrc, Path.Combine(stagingDir, "Dockerfile"), overwrite: true);
        var ignoreSrc = Path.Combine(output, ".dockerignore");
        if (File.Exists(ignoreSrc))
        {
            File.Copy(ignoreSrc, Path.Combine(stagingDir, ".dockerignore"), overwrite: true);
        }

        var buildCtx = new BuildContext
        {
            ContextDirectory = stagingDir,
            DockerfilePath = Path.Combine(stagingDir, "Dockerfile"),
            ImageName = plan.ImageName,
            ImageTag = plan.ImageTag,
            Platforms = request.Platforms is { Count: > 0 } ? request.Platforms.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() : Array.Empty<string>()
        };

        progress?.Report($"building {plan.FullImageReference}");
        await foreach (var line in engine.BuildAsync(buildCtx, ct))
        {
            progress?.Report(line);
        }

        var image = await engine.GetImageAsync(plan.FullImageReference, ct);

        return new BuildPipelineResult
        {
            OutputDirectory = output,
            GeneratedFileCount = genResult.WrittenFiles.Count,
            ImageReference = plan.FullImageReference,
            ImageSizeBytes = image?.SizeBytes,
            EngineName = info.Name,
            EngineVersion = info.Version
        };
    }

    private static async Task CopyDirectoryAsync(string source, string destination, string? excludeRoot, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var excludeFull = excludeRoot is null ? null : Path.GetFullPath(excludeRoot);

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (IsUnderneath(Path.GetFullPath(dir), excludeFull))
                {
                    continue;
                }
                var target = dir.Replace(source, destination, StringComparison.Ordinal);
                Directory.CreateDirectory(target);
            }
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (IsUnderneath(Path.GetFullPath(file), excludeFull))
                {
                    continue;
                }
                var target = file.Replace(source, destination, StringComparison.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }, ct);
    }

    private static bool IsUnderneath(string candidate, string? root)
    {
        if (root is null)
        {
            return false;
        }
        var normalized = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalized + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase);
    }
}
