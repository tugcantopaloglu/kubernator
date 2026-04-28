namespace Kubernator.Core.Models;

public sealed record DetectionResult
{
    public required string SourcePath { get; init; }
    public AppKind Kind { get; init; }
    public AppFlavor Flavor { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyList<string> Signals { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static DetectionResult None(string path) => new()
    {
        SourcePath = path,
        Kind = AppKind.Unknown,
        Flavor = AppFlavor.Unknown,
        Confidence = 0
    };
}
