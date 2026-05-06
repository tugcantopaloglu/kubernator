namespace Kubernator.Core.AirGapped;

public sealed record ImageBundleManifest
{
    public required string SchemaVersion { get; init; }
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<ImageBundleEntry> Images { get; init; }
}

public sealed record ImageBundleEntry
{
    public required string Reference { get; init; }
    public required string TarRelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public required string ImageId { get; init; }
    public string? Platform { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
