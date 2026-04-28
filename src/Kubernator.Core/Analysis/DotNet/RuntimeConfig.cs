namespace Kubernator.Core.Analysis.DotNet;

internal sealed record RuntimeConfig
{
    public string? Tfm { get; init; }
    public string? RollForward { get; init; }
    public IReadOnlyList<RuntimeFrameworkRef> Frameworks { get; init; } = [];
    public IReadOnlyDictionary<string, string> ConfigProperties { get; init; } =
        new Dictionary<string, string>();

    public bool InvariantGlobalization =>
        ConfigProperties.TryGetValue("System.Globalization.Invariant", out var v) &&
        bool.TryParse(v, out var parsed) && parsed;
}

internal sealed record RuntimeFrameworkRef
{
    public required string Name { get; init; }
    public string? Version { get; init; }
}
