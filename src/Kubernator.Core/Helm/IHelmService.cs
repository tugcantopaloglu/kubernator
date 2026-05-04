using Kubernator.Core.Strategy;

namespace Kubernator.Core.Helm;

public interface IHelmService
{
    Task<HelmGenerationResult> GenerateAsync(BuildPlan plan, HelmOptions options, CancellationToken ct = default);
}
