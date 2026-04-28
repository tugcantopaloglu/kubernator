namespace Kubernator.Core.Models;

public sealed record AppDescriptor
{
    public required string SourcePath { get; init; }
    public AppKind Kind { get; init; }
    public AppFlavor Flavor { get; init; }
    public required RuntimeInfo Runtime { get; init; }
    public NetworkInfo Network { get; init; } = new();
    public DependencyInfo Dependencies { get; init; } = new();
    public EntryPoint? EntryPoint { get; init; }
    public IReadOnlyDictionary<string, string> EnvironmentHints { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public double DetectionConfidence { get; init; }
}
