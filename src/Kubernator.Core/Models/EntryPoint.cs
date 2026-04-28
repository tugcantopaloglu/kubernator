namespace Kubernator.Core.Models;

public sealed record EntryPoint
{
    public required string Path { get; init; }
    public string? AssemblyName { get; init; }
    public string? StartupCommand { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
