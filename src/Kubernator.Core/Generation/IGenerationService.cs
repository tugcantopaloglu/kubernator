using Kubernator.Core.Strategy;

namespace Kubernator.Core.Generation;

public interface IGenerationService
{
    Task<GenerationResult> GenerateAsync(BuildPlan plan, GenerationOptions options, CancellationToken ct = default);
}
