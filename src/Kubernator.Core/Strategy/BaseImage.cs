namespace Kubernator.Core.Strategy;

public sealed record BaseImage
{
    public required string Registry { get; init; }
    public required string Repository { get; init; }
    public required string Tag { get; init; }
    public string? Digest { get; init; }
    public required string DisplayName { get; init; }
    public required bool NonRootByDefault { get; init; }
    public bool RootlessSupportsExec { get; init; }
    public bool HasShell { get; init; }
    public required long DefaultUserId { get; init; }
    public required long DefaultGroupId { get; init; }
    public string? Notes { get; init; }

    public string Reference => Digest is { Length: > 0 }
        ? $"{Registry}/{Repository}:{Tag}@{Digest}"
        : $"{Registry}/{Repository}:{Tag}";
}

public static class AllowedRegistries
{
    public const string Microsoft = "mcr.microsoft.com";
    public const string Chainguard = "cgr.dev";
    public const string GoogleDistroless = "gcr.io";
    public const string KubernetesOfficial = "registry.k8s.io";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Microsoft,
        Chainguard,
        GoogleDistroless,
        KubernetesOfficial
    };

    public static bool IsAllowed(string registry) => All.Contains(registry);
}
