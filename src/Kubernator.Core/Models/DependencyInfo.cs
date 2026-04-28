namespace Kubernator.Core.Models;

public sealed record ManagedDependency
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Source { get; init; }
}

public sealed record NativeDependency
{
    public required string Name { get; init; }
    public string? Origin { get; init; }
}

public sealed record DependencyInfo
{
    public IReadOnlyList<ManagedDependency> Managed { get; init; } = [];
    public IReadOnlyList<NativeDependency> Native { get; init; } = [];
    public bool RequiresIcu { get; init; }
    public bool RequiresTimezone { get; init; }
    public bool RequiresGdiPlus { get; init; }
}
