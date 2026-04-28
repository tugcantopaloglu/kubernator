namespace Kubernator.Core.Analysis.DotNet;

internal sealed record DepsJsonModel
{
    public string? RuntimeTargetName { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public IReadOnlyList<DepsLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<string> NativeFiles { get; init; } = [];
}

internal sealed record DepsLibrary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Type { get; init; }
}
