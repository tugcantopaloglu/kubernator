using Kubernator.Core.Strategy;

namespace Kubernator.Core.Kustomize;

public interface IKustomizeService
{
    Task<KustomizeResult> GenerateAsync(BuildPlan plan, KustomizeOptions options, CancellationToken ct = default);
}
