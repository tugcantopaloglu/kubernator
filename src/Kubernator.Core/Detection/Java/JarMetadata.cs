namespace Kubernator.Core.Detection.Java;

internal sealed record JarMetadata
{
    public required string JarPath { get; init; }
    public string? MainClass { get; init; }
    public string? StartClass { get; init; }
    public string? ImplementationVersion { get; init; }
    public string? ImplementationTitle { get; init; }
    public bool IsSpringBoot { get; init; }
    public bool IsQuarkus { get; init; }
    public bool IsWar { get; init; }
    public IReadOnlyDictionary<string, string> ApplicationProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
