namespace Kubernator.Core.Deployment;

public sealed record ClusterContext
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
    public bool LooksProduction => LooksLikeProduction(Name);

    public static bool LooksLikeProduction(string name)
    {
        var lowered = name.ToLowerInvariant();
        return lowered.Contains("prod", StringComparison.Ordinal)
            || lowered.Contains("production", StringComparison.Ordinal)
            || lowered.Contains("live", StringComparison.Ordinal);
    }
}

public sealed record DeployOptions
{
    public required string ManifestsDirectory { get; init; }
    public required string Context { get; init; }
    public string Namespace { get; init; } = "default";
    public bool DryRun { get; init; }
    public bool AllowProduction { get; init; }
    public bool CreateNamespace { get; init; } = true;
    public string KubectlBinary { get; init; } = "kubectl";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record DeployResult
{
    public required bool Ok { get; init; }
    public required string Context { get; init; }
    public required string Namespace { get; init; }
    public required bool DryRun { get; init; }
    public required IReadOnlyList<string> AppliedResources { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

public sealed record ClusterRegistration
{
    public required string Name { get; init; }
    public required string ServerUrl { get; init; }
    public required string CaCertificatePem { get; init; }
    public string? Token { get; init; }
    public string? ClientCertificatePem { get; init; }
    public string? ClientKeyPem { get; init; }
    public string? Namespace { get; init; }
    public bool SkipTlsVerify { get; init; }
}

public sealed record ClusterRegistrationResult
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> AppliedSteps { get; init; }
}

public interface IClusterApplier
{
    Task<IReadOnlyList<ClusterContext>> ListContextsAsync(string kubectlBinary = "kubectl", CancellationToken ct = default);

    Task<DeployResult> ApplyAsync(DeployOptions options, IProgress<string>? progress = null, CancellationToken ct = default);

    Task<ClusterRegistrationResult> RegisterContextAsync(ClusterRegistration registration, string kubectlBinary = "kubectl", CancellationToken ct = default);
}
