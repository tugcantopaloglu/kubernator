using Kubernator.Core.Models;
using Kubernator.Core.Pipelines;
using Kubernator.Core.Tests.Fixtures;

namespace Kubernator.Core.Tests.Pipelines;

public sealed class PipelineServiceTests
{
    private readonly PipelineService service = new();

    [Fact]
    public async Task GitHub_actions_for_dotnet_emits_workflow()
    {
        using var t = TempPublishOutput.Create();
        var options = new PipelineOptions
        {
            AppKind = AppKind.DotNet,
            ImageName = "myapp",
            ImageTag = "1.0.0",
            Registry = "registry.example.com",
            SignBundle = true
        };

        var result = await service.GenerateAsync(PipelineTarget.GitHubActions, options, t.Path);

        var path = result.WrittenFiles.Single();
        path.Should().EndWith("kubernator.yml");
        var yml = await File.ReadAllTextAsync(path);
        yml.Should().Contain("name: kubernator-myapp");
        yml.Should().Contain("actions/setup-dotnet@v4");
        yml.Should().Contain("dotnet publish");
        yml.Should().Contain("kubernator bundle");
        yml.Should().Contain("kubernator sign");
        yml.Should().Contain("upload-artifact@v4");
    }

    [Fact]
    public async Task GitLab_for_node_uses_node_image_and_dind()
    {
        using var t = TempPublishOutput.Create();
        var options = new PipelineOptions
        {
            AppKind = AppKind.NodeJs,
            ImageName = "site",
            ImageTag = "0.1.0"
        };

        var result = await service.GenerateAsync(PipelineTarget.GitLabCi, options, t.Path);

        var path = result.WrittenFiles.Single();
        path.Should().EndWith(".gitlab-ci.yml");
        var yml = await File.ReadAllTextAsync(path);
        (yml.Contains("image: node:20", StringComparison.Ordinal) || yml.Contains("image: \"node:20\"", StringComparison.Ordinal)).Should().BeTrue();
        yml.Should().Contain("docker:dind");
        yml.Should().Contain("npm ci");
        yml.Should().Contain("kubernator bundle");
    }

    [Fact]
    public async Task AzureDevOps_for_python_uses_python_task()
    {
        using var t = TempPublishOutput.Create();
        var options = new PipelineOptions
        {
            AppKind = AppKind.Python,
            ImageName = "api",
            ImageTag = "0.0.1"
        };

        var result = await service.GenerateAsync(PipelineTarget.AzureDevOps, options, t.Path);

        var yml = await File.ReadAllTextAsync(result.WrittenFiles.Single());
        yml.Should().Contain("UsePythonVersion@0");
        yml.Should().Contain("pip install");
        yml.Should().Contain("kubernator bundle");
    }

    [Fact]
    public async Task Tekton_for_java_emits_pipeline_and_task()
    {
        using var t = TempPublishOutput.Create();
        var options = new PipelineOptions
        {
            AppKind = AppKind.Java,
            ImageName = "demo",
            ImageTag = "1.2.3"
        };

        var result = await service.GenerateAsync(PipelineTarget.Tekton, options, t.Path);

        result.WrittenFiles.Should().HaveCount(2);
        var pipeline = await File.ReadAllTextAsync(result.WrittenFiles.First(f => f.EndsWith("pipeline.yaml", StringComparison.Ordinal)));
        var task = await File.ReadAllTextAsync(result.WrittenFiles.First(f => f.EndsWith("task.yaml", StringComparison.Ordinal)));
        pipeline.Should().Contain("kind: Pipeline");
        pipeline.Should().Contain("demo-pipeline");
        task.Should().Contain("kind: Task");
        task.Should().Contain("mvn -B");
        task.Should().Contain("kubernator bundle");
    }
}
