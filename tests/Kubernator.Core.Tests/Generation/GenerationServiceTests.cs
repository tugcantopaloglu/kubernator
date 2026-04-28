using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.Generation;

public sealed class GenerationServiceTests
{
    private readonly StrategySelector strategy = new();
    private readonly GenerationService generation = new();

    [Fact]
    public async Task Generates_dockerfile_dockerignore_deployment_service_networkpolicy_for_aspnet()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new GenerationOptions
        {
            OutputDirectory = temp.Path,
            Namespace = "demo",
            Replicas = 3
        };

        var result = await generation.GenerateAsync(plan, options);

        result.WrittenFiles.Should().HaveCount(5);
        File.Exists(Path.Combine(temp.Path, "Dockerfile")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, ".dockerignore")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "kubernetes", "deployment.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "kubernetes", "service.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "kubernetes", "networkpolicy.yaml")).Should().BeTrue();
    }

    [Fact]
    public async Task Dockerfile_uses_allowed_registry_and_non_root()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        var dockerfile = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Dockerfile"));
        dockerfile.Should().StartWith("FROM mcr.microsoft.com/");
        dockerfile.Should().Contain("USER 1654:1654");
        dockerfile.Should().Contain("ENTRYPOINT [\"dotnet\"");
        dockerfile.Should().NotContain("#");
    }

    [Fact]
    public async Task Deployment_enforces_security_hardening()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        var deployment = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "deployment.yaml"));
        deployment.Should().Contain("runAsNonRoot: true");
        deployment.Should().Contain("readOnlyRootFilesystem: true");
        deployment.Should().Contain("allowPrivilegeEscalation: false");
        deployment.Should().Contain("- ALL");
        deployment.Should().Contain("seccompProfile:");
        deployment.Should().Contain("automountServiceAccountToken: false");
        deployment.Should().NotContain("# ");
    }

    [Fact]
    public async Task Skips_service_for_console_app()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.Console());

        var result = await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        File.Exists(Path.Combine(temp.Path, "kubernetes", "service.yaml")).Should().BeFalse();
        result.WrittenFiles.Should().NotContain(f => f.EndsWith("service.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelfContained_uses_chainguard_static_and_uid_65532()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.SelfContained());

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        var dockerfile = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Dockerfile"));
        dockerfile.Should().StartWith("FROM cgr.dev/chainguard/static");
        dockerfile.Should().Contain("USER 65532:65532");
    }
}
