using Kubernator.Core.Pipelines.Emitters;

namespace Kubernator.Core.Pipelines;

public sealed class PipelineService : IPipelineService
{
    public async Task<PipelineGenerationResult> GenerateAsync(
        PipelineTarget target,
        PipelineOptions options,
        string outputDirectory,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        switch (target)
        {
            case PipelineTarget.GitHubActions:
                {
                    var workflowDir = Path.Combine(outputDirectory, ".github", "workflows");
                    Directory.CreateDirectory(workflowDir);
                    var path = Path.Combine(workflowDir, "kubernator.yml");
                    await File.WriteAllTextAsync(path, GitHubActionsEmitter.Emit(options), ct);
                    written.Add(path);
                    break;
                }
            case PipelineTarget.GitLabCi:
                {
                    var path = Path.Combine(outputDirectory, ".gitlab-ci.yml");
                    await File.WriteAllTextAsync(path, GitLabCiEmitter.Emit(options), ct);
                    written.Add(path);
                    break;
                }
            case PipelineTarget.AzureDevOps:
                {
                    var path = Path.Combine(outputDirectory, "azure-pipelines.yml");
                    await File.WriteAllTextAsync(path, AzureDevOpsEmitter.Emit(options), ct);
                    written.Add(path);
                    break;
                }
            case PipelineTarget.Tekton:
                {
                    var dir = Path.Combine(outputDirectory, "tekton");
                    Directory.CreateDirectory(dir);
                    var (pipeline, task) = TektonEmitter.Emit(options);
                    var pipelinePath = Path.Combine(dir, "pipeline.yaml");
                    var taskPath = Path.Combine(dir, "task.yaml");
                    await File.WriteAllTextAsync(pipelinePath, pipeline, ct);
                    await File.WriteAllTextAsync(taskPath, task, ct);
                    written.Add(pipelinePath);
                    written.Add(taskPath);
                    break;
                }
            default:
                throw new NotSupportedException($"Unknown pipeline target: {target}");
        }

        return new PipelineGenerationResult { WrittenFiles = written };
    }
}
