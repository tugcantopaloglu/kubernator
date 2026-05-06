using Kubernator.Core.Containers;

namespace Kubernator.Core.AirGapped;

public sealed record ImageBundleOptions
{
    public required IReadOnlyList<string> References { get; init; }
    public required string OutputDirectory { get; init; }
    public string? Platform { get; init; }
    public bool ForcePull { get; init; }
    public bool CombineIntoSingleArchive { get; init; }
    public string CombinedArchiveName { get; init; } = "kubernator-images.tar.gz";
}

public sealed record ImageBundleResult
{
    public required string OutputDirectory { get; init; }
    public required string ManifestPath { get; init; }
    public required ImageBundleManifest Manifest { get; init; }
    public string? CombinedArchivePath { get; init; }
}

public sealed record ImagePushPlan
{
    public required string SourceReference { get; init; }
    public required string TargetReference { get; init; }
    public string? TarRelativePath { get; init; }
}

public sealed record ImageRehostOptions
{
    public required string BundleDirectory { get; init; }
    public required string TargetRegistry { get; init; }
    public string? TargetNamespace { get; init; }
    public bool LoadBeforePush { get; init; } = true;
    public bool RewriteManifestImages { get; init; } = true;
    public string? ManifestsDirectory { get; init; }
}

public sealed record ImageRehostResult
{
    public required IReadOnlyList<ImagePushPlan> Pushed { get; init; }
    public IReadOnlyList<string> RewrittenManifestFiles { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool Ok => Errors.Count == 0;
}

public interface IImageBundleService
{
    Task<ImageBundleResult> PullAsync(
        ImageBundleOptions options,
        IContainerEngine engine,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<ImageRehostResult> RehostAsync(
        ImageRehostOptions options,
        IContainerEngine engine,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<ImageBundleManifest?> ReadManifestAsync(string bundleDirectory, CancellationToken ct = default);
}
