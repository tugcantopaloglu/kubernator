namespace Kubernator.Core.Models;

public sealed record RuntimeInfo
{
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Tfm { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public TargetOs TargetOs { get; init; }
    public TargetArchitecture TargetArch { get; init; }
    public PublishMode PublishMode { get; init; }
    public IReadOnlyList<string> FrameworkReferences { get; init; } = [];
}
