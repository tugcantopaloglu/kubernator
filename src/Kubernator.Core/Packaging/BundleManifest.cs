namespace Kubernator.Core.Packaging;

public sealed record BundleManifest
{
    public required string SchemaVersion { get; init; }
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required AppInfo App { get; init; }
    public required IReadOnlyList<ImageEntry> Images { get; init; }
    public required IReadOnlyList<FileEntry> Files { get; init; }
    public required string KubernetesNamespace { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record AppInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
    public required string Flavor { get; init; }
    public required string TargetOs { get; init; }
    public required string TargetArch { get; init; }
    public required string Tfm { get; init; }
}

public sealed record ImageEntry
{
    public required string Reference { get; init; }
    public required string TarRelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public string? ImageId { get; init; }
}

public sealed record FileEntry
{
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}
