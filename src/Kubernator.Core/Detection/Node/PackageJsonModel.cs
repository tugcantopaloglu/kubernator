namespace Kubernator.Core.Detection.Node;

internal sealed record PackageJsonModel
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Main { get; init; }
    public string? Type { get; init; }
    public IReadOnlyDictionary<string, string> Scripts { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Dependencies { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> DevDependencies { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public string? NodeEngine { get; init; }
    public IReadOnlyDictionary<string, string> Bin { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
