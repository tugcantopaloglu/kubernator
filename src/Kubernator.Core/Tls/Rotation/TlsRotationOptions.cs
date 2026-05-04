namespace Kubernator.Core.Tls.Rotation;

public sealed record TlsRotationOptions
{
    public required string OutputDirectory { get; init; }
    public required string SecretName { get; init; }
    public required string Hostname { get; init; }
    public string Namespace { get; init; } = "default";
    public string Schedule { get; init; } = "0 3 1 * *";
    public int DaysValid { get; init; } = 90;
    public IReadOnlyList<string> AdditionalHostnames { get; init; } = [];
    public string Image { get; init; } = "cgr.dev/chainguard/wolfi-base:latest";
    public string? ServiceAccountName { get; init; }
    public string? CronJobName { get; init; }
    public int SuccessfulJobsHistoryLimit { get; init; } = 3;
    public int FailedJobsHistoryLimit { get; init; } = 1;
}

public sealed record TlsRotationResult
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required string ResolvedServiceAccountName { get; init; }
    public required string ResolvedCronJobName { get; init; }
}
