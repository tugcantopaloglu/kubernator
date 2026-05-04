namespace Kubernator.Core.Updates;

public sealed record ReleaseManifest
{
    public required string Version { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required IReadOnlyList<ReleaseArtifact> Artifacts { get; init; }
    public string? Notes { get; init; }
    public string? MinimumUpgradableFrom { get; init; }
}

public sealed record ReleaseArtifact
{
    public required string RuntimeIdentifier { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public string? FileName { get; init; }
}

public sealed record UpdateCheckResult
{
    public required string CurrentVersion { get; init; }
    public required ReleaseManifest Manifest { get; init; }
    public required bool UpgradeAvailable { get; init; }
}

public sealed record UpdateApplyResult
{
    public required string OldExecutablePath { get; init; }
    public required string NewExecutablePath { get; init; }
    public required string DownloadedFromUrl { get; init; }
    public required string Sha256 { get; init; }
    public required string ToVersion { get; init; }
}
