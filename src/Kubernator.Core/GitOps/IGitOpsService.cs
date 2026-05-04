using Kubernator.Core.Strategy;

namespace Kubernator.Core.GitOps;

public interface IGitOpsService
{
    Task<GitOpsResult> GenerateAsync(BuildPlan plan, GitOpsOptions options, CancellationToken ct = default);
}
