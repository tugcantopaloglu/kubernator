namespace Kubernator.Core.Pipelines;

public interface IPipelineService
{
    Task<PipelineGenerationResult> GenerateAsync(
        PipelineTarget target,
        PipelineOptions options,
        string outputDirectory,
        CancellationToken ct = default);
}
