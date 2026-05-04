using Kubernator.Core.Generation;
using Kubernator.Core.Kustomize;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.Kustomize;

public sealed class KustomizeServiceTests
{
    private readonly StrategySelector strategy = new();
    private readonly KustomizeService service = new();

    [Fact]
    public async Task Generates_base_with_kustomization_yaml_listing_resources()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new KustomizeOptions
        {
            OutputDirectory = temp.Path,
            BaseNamespace = "demo",
            Overlays = ["production"]
        };

        var result = await service.GenerateAsync(plan, options);

        File.Exists(Path.Combine(result.BaseDirectory, "kustomization.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(result.BaseDirectory, "deployment.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(result.BaseDirectory, "service.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(result.BaseDirectory, "networkpolicy.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "overlays", "production", "kustomization.yaml")).Should().BeTrue();

        var baseKustomization = await File.ReadAllTextAsync(Path.Combine(result.BaseDirectory, "kustomization.yaml"));
        baseKustomization.Should().Contain("apiVersion: kustomize.config.k8s.io/v1beta1");
        baseKustomization.Should().Contain("kind: Kustomization");
        baseKustomization.Should().Contain("- deployment.yaml");
        baseKustomization.Should().Contain("- service.yaml");
        baseKustomization.Should().Contain("namespace: demo");
        baseKustomization.Should().Contain($"name: {plan.ImageName}");
        baseKustomization.Should().Contain($"newTag: {plan.ImageTag}");
    }

    [Fact]
    public async Task Production_overlay_patches_replicas_and_namespace()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new KustomizeOptions
        {
            OutputDirectory = temp.Path,
            BaseNamespace = "demo",
            Overlays = ["production"],
            Replicas = 1,
            Scaling = new ScalingOptions { HpaMinReplicas = 5 }
        };

        await service.GenerateAsync(plan, options);

        var prod = await File.ReadAllTextAsync(Path.Combine(temp.Path, "overlays", "production", "kustomization.yaml"));
        prod.Should().Contain("- ../../base");
        prod.Should().Contain("-production");
        prod.Should().Contain("path: /spec/replicas");
        prod.Should().Contain("value: 5");
    }

    [Fact]
    public async Task Includes_hpa_pdb_when_scaling_enabled()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new KustomizeOptions
        {
            OutputDirectory = temp.Path,
            Scaling = new ScalingOptions
            {
                HpaMinReplicas = 2,
                HpaMaxReplicas = 8,
                PdbMinAvailable = 1
            },
            Overlays = ["production"]
        };

        var result = await service.GenerateAsync(plan, options);

        File.Exists(Path.Combine(result.BaseDirectory, "hpa.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(result.BaseDirectory, "pdb.yaml")).Should().BeTrue();
        var baseKustomization = await File.ReadAllTextAsync(Path.Combine(result.BaseDirectory, "kustomization.yaml"));
        baseKustomization.Should().Contain("- hpa.yaml");
        baseKustomization.Should().Contain("- pdb.yaml");
    }

    [Fact]
    public async Task Includes_ingress_and_tls_when_exposure_set()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "app.example.com",
                TlsMode = TlsMode.SelfSigned
            }
        });
        var options = new KustomizeOptions
        {
            OutputDirectory = temp.Path,
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "app.example.com",
                TlsMode = TlsMode.SelfSigned
            },
            Overlays = ["production"]
        };

        var result = await service.GenerateAsync(plan, options);

        File.Exists(Path.Combine(result.BaseDirectory, "ingress.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(result.BaseDirectory, "tls-secret.yaml")).Should().BeTrue();
        var baseKustomization = await File.ReadAllTextAsync(Path.Combine(result.BaseDirectory, "kustomization.yaml"));
        baseKustomization.Should().Contain("- ingress.yaml");
        baseKustomization.Should().Contain("- tls-secret.yaml");
    }
}
