namespace Kubernator.Core.Detection.DotNet;

internal sealed record DotNetPublishLayout
{
    public required string RootPath { get; init; }
    public required string AssemblyBaseName { get; init; }
    public required string DepsJsonPath { get; init; }
    public string? RuntimeConfigPath { get; init; }
    public string? MainAssemblyPath { get; init; }
    public string? AppHostPath { get; init; }
    public bool HasRuntimeConfig => !string.IsNullOrEmpty(RuntimeConfigPath);
    public bool HasMainAssembly => !string.IsNullOrEmpty(MainAssemblyPath);
}
