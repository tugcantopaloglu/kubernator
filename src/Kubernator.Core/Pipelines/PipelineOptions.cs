using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines;

public enum PipelineTarget
{
    GitHubActions,
    GitLabCi,
    AzureDevOps,
    Tekton
}

public sealed record PipelineOptions
{
    public required AppKind AppKind { get; init; }
    public required string ImageName { get; init; }
    public required string ImageTag { get; init; }
    public string Registry { get; init; } = "registry.example.com";
    public string Namespace { get; init; } = "default";
    public string SourcePathInRepo { get; init; } = ".";
    public string PublishPath { get; init; } = "./publish";
    public string BundleArtifactName { get; init; } = "bundle";
    public bool SignBundle { get; init; }
    public bool RunVerify { get; init; } = true;
    public string KubernatorVersion { get; init; } = "0.1.0";
}

public sealed record PipelineGenerationResult
{
    public required IReadOnlyList<string> WrittenFiles { get; init; }
}
