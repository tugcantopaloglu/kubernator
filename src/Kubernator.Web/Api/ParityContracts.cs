using Kubernator.Core.Deployment;
using Kubernator.Core.Monitoring;
using Kubernator.Core.Validation;
using Kubernator.Web.Services;

namespace Kubernator.Web.Api;

public sealed record BuildRequest
{
    public required string Path { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public bool NoBuild { get; init; }
    public IReadOnlyList<string>? Platforms { get; init; }
}

public sealed record BuildResultDto
{
    public required string OutputDirectory { get; init; }
    public required int GeneratedFileCount { get; init; }
    public string? ImageReference { get; init; }
    public long? ImageSizeBytes { get; init; }
    public string? EngineName { get; init; }
    public string? EngineVersion { get; init; }

    public static BuildResultDto From(BuildPipelineResult r) => new()
    {
        OutputDirectory = r.OutputDirectory,
        GeneratedFileCount = r.GeneratedFileCount,
        ImageReference = r.ImageReference,
        ImageSizeBytes = r.ImageSizeBytes,
        EngineName = r.EngineName,
        EngineVersion = r.EngineVersion
    };
}

public sealed record BundleCreateRequest
{
    public required string Path { get; init; }
    public required string OutputBundlePath { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public string? Namespace { get; init; }
    public int Replicas { get; init; } = 1;
    public bool IncludeSbom { get; init; } = true;
    public bool BuildIfMissing { get; init; } = true;
    public bool KeepScratch { get; init; }
}

public sealed record BundleCreateResultDto
{
    public required string BundlePath { get; init; }
    public required long BundleSizeBytes { get; init; }
    public required string BundleSha256 { get; init; }
    public required string ImageReference { get; init; }
}

public sealed record DeployRequest
{
    public required string ManifestsDirectory { get; init; }
    public required string Context { get; init; }
    public string? Namespace { get; init; }
    public bool DryRun { get; init; } = true;
    public bool AllowProduction { get; init; }
    public bool CreateNamespace { get; init; } = true;
    public string? KubectlBinary { get; init; }
}

public sealed record DeployResultDto
{
    public required bool Ok { get; init; }
    public required string Context { get; init; }
    public required string Namespace { get; init; }
    public required bool DryRun { get; init; }
    public required IReadOnlyList<string> AppliedResources { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static DeployResultDto From(DeployResult r) => new()
    {
        Ok = r.Ok,
        Context = r.Context,
        Namespace = r.Namespace,
        DryRun = r.DryRun,
        AppliedResources = r.AppliedResources,
        Errors = r.Errors
    };
}

public sealed record ContextListResponse
{
    public required IReadOnlyList<ContextDto> Contexts { get; init; }
}

public sealed record ContextDto
{
    public required string Name { get; init; }
    public required bool IsCurrent { get; init; }
    public required bool LooksProduction { get; init; }
}

public sealed record MonitorRequest
{
    public required string Context { get; init; }
    public string? Namespace { get; init; }
    public bool IncludeMetrics { get; init; } = true;
    public bool IncludePods { get; init; } = true;
    public bool IncludeIngress { get; init; } = true;
    public bool IncludeNetworkPolicies { get; init; } = true;
    public bool IncludeServices { get; init; } = true;
    public string? KubectlBinary { get; init; }
}

public sealed record MonitorResponse
{
    public required string Context { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required int ReadyNodes { get; init; }
    public required int RunningPods { get; init; }
    public required int FailedPods { get; init; }
    public required int NodeCount { get; init; }
    public required int PodCount { get; init; }
    public required int IngressCount { get; init; }
    public required int NetworkPolicyCount { get; init; }
    public required int ServiceCount { get; init; }
    public required bool MetricsServerAvailable { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public string? ApiVersion { get; init; }

    public static MonitorResponse From(ClusterSnapshot s) => new()
    {
        Context = s.Context,
        CapturedAt = s.CapturedAt,
        ReadyNodes = s.ReadyNodes,
        RunningPods = s.RunningPods,
        FailedPods = s.FailedPods,
        NodeCount = s.Nodes.Count,
        PodCount = s.Pods.Count,
        IngressCount = s.Ingresses.Count,
        NetworkPolicyCount = s.NetworkPolicies.Count,
        ServiceCount = s.Services.Count,
        MetricsServerAvailable = s.MetricsServerAvailable,
        Warnings = s.Warnings,
        ApiVersion = s.ApiVersion
    };
}

public sealed record ValidateRequest
{
    public required string Path { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public string? ClusterName { get; init; }
    public string? Namespace { get; init; }
    public bool KeepCluster { get; init; }
    public bool ReuseExistingCluster { get; init; }
    public string? ProbePath { get; init; }
    public int? ProbePort { get; init; }
}

public sealed record ValidateResultDto
{
    public required bool Ok { get; init; }
    public required string ClusterName { get; init; }
    public required bool ClusterStillRunning { get; init; }
    public required IReadOnlyList<ValidationStepDto> Steps { get; init; }

    public static ValidateResultDto From(ValidationResult r) => new()
    {
        Ok = r.Ok,
        ClusterName = r.ClusterName,
        ClusterStillRunning = r.ClusterStillRunning,
        Steps = r.Steps.Select(ValidationStepDto.From).ToArray()
    };
}

public sealed record ValidationStepDto
{
    public required string Name { get; init; }
    public required bool Ok { get; init; }
    public required long DurationMs { get; init; }
    public string? Error { get; init; }

    public static ValidationStepDto From(ValidationStep s) => new()
    {
        Name = s.Name,
        Ok = s.Ok,
        DurationMs = (long)s.Duration.TotalMilliseconds,
        Error = s.Error
    };
}
