using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.Generation;

public sealed class AutoscalingEmitterTests
{
    private readonly StrategySelector strategy = new();
    private readonly GenerationService generation = new();

    [Fact]
    public async Task Hpa_emitted_with_cpu_and_memory_targets()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new GenerationOptions
        {
            OutputDirectory = temp.Path,
            Scaling = new ScalingOptions
            {
                HpaMinReplicas = 3,
                HpaMaxReplicas = 12,
                HpaTargetCpuUtilization = 70,
                HpaTargetMemoryUtilization = 80
            }
        };

        await generation.GenerateAsync(plan, options);

        var hpa = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "hpa.yaml"));
        hpa.Should().Contain("apiVersion: autoscaling/v2");
        hpa.Should().Contain("kind: HorizontalPodAutoscaler");
        hpa.Should().Contain("minReplicas: 3");
        hpa.Should().Contain("maxReplicas: 12");
        hpa.Should().Contain("averageUtilization: 70");
        hpa.Should().Contain("averageUtilization: 80");
    }

    [Fact]
    public async Task Pdb_emitted_with_min_available()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new GenerationOptions
        {
            OutputDirectory = temp.Path,
            Scaling = new ScalingOptions { PdbMinAvailable = 2 }
        };

        await generation.GenerateAsync(plan, options);

        var pdb = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "pdb.yaml"));
        pdb.Should().Contain("apiVersion: policy/v1");
        pdb.Should().Contain("kind: PodDisruptionBudget");
        pdb.Should().Contain("minAvailable: 2");
    }

    [Fact]
    public async Task Pdb_emits_percent_when_set()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new GenerationOptions
        {
            OutputDirectory = temp.Path,
            Scaling = new ScalingOptions { PdbMinAvailablePercent = "50%" }
        };

        await generation.GenerateAsync(plan, options);

        var pdb = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "pdb.yaml"));
        pdb.Should().MatchRegex("minAvailable: \"?50%\"?");
    }

    [Fact]
    public async Task Files_skipped_when_scaling_not_set()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        File.Exists(Path.Combine(temp.Path, "kubernetes", "hpa.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, "kubernetes", "pdb.yaml")).Should().BeFalse();
    }
}
