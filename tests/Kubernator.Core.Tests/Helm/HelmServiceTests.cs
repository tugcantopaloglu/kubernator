using System.Formats.Tar;
using System.IO.Compression;
using Kubernator.Core.Generation;
using Kubernator.Core.Helm;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.Helm;

public sealed class HelmServiceTests
{
    private readonly StrategySelector strategy = new();
    private readonly HelmService service = new();

    [Fact]
    public async Task Generates_chart_with_expected_structure()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new HelmOptions
        {
            OutputDirectory = temp.Path,
            ChartName = "demo",
            ChartVersion = "0.1.0"
        };

        var result = await service.GenerateAsync(plan, options);

        var chartDir = Path.Combine(temp.Path, "demo");
        Directory.Exists(chartDir).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "Chart.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "values.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "_helpers.tpl")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "deployment.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "service.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "ingress.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "hpa.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "pdb.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(chartDir, "templates", "networkpolicy.yaml")).Should().BeTrue();
    }

    [Fact]
    public async Task Chart_yaml_has_version_and_appVersion()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new HelmOptions
        {
            OutputDirectory = temp.Path,
            ChartName = "myapp",
            ChartVersion = "2.5.0"
        };

        await service.GenerateAsync(plan, options);

        var chart = await File.ReadAllTextAsync(Path.Combine(temp.Path, "myapp", "Chart.yaml"));
        chart.Should().Contain("apiVersion: v2");
        chart.Should().Contain("name: myapp");
        chart.Should().Contain("version: 2.5.0");
        chart.Should().Contain($"appVersion: {plan.ImageTag}");
    }

    [Fact]
    public async Task Values_yaml_reflects_exposure_and_scaling()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "site.example.com",
                TlsMode = TlsMode.SelfSigned
            }
        });
        var options = new HelmOptions
        {
            OutputDirectory = temp.Path,
            ChartName = "site",
            Scaling = new ScalingOptions
            {
                HpaMinReplicas = 2,
                HpaMaxReplicas = 8,
                HpaTargetCpuUtilization = 60,
                PdbMinAvailable = 1
            },
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "site.example.com",
                TlsMode = TlsMode.SelfSigned
            }
        };

        await service.GenerateAsync(plan, options);

        var values = await File.ReadAllTextAsync(Path.Combine(temp.Path, "site", "values.yaml"));
        values.Should().Contain("ingress:");
        values.Should().Contain("enabled: true");
        values.Should().Contain("- host: site.example.com");
        values.Should().Contain("autoscaling:");
        values.Should().Contain("minReplicas: 2");
        values.Should().Contain("maxReplicas: 8");
        values.Should().Contain("targetCPUUtilizationPercentage: 60");
        values.Should().Contain("podDisruptionBudget:");
        values.Should().Contain("minAvailable: 1");
    }

    [Fact]
    public async Task Package_produces_tgz_with_chart_directory_inside()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var options = new HelmOptions
        {
            OutputDirectory = temp.Path,
            ChartName = "demo",
            ChartVersion = "0.0.1",
            Package = true
        };

        var result = await service.GenerateAsync(plan, options);

        result.PackageFile.Should().NotBeNull();
        File.Exists(result.PackageFile!).Should().BeTrue();

        var entries = new List<string>();
        await using var fs = File.OpenRead(result.PackageFile!);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await using var tar = new TarReader(gz);
        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync()) is not null)
        {
            entries.Add(entry.Name.Replace('\\', '/'));
        }
        entries.Should().Contain(e => e.EndsWith("Chart.yaml", StringComparison.Ordinal));
        entries.Should().Contain(e => e.EndsWith("values.yaml", StringComparison.Ordinal));
        entries.Should().Contain(e => e.EndsWith("templates/deployment.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Templates_use_helm_helpers_and_values_references()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new HelmOptions { OutputDirectory = temp.Path, ChartName = "demo" });

        var deployment = await File.ReadAllTextAsync(Path.Combine(temp.Path, "demo", "templates", "deployment.yaml"));
        deployment.Should().Contain("{{ include \"kubernator.fullname\" . }}");
        deployment.Should().Contain("{{ .Values.replicaCount }}");
        deployment.Should().Contain("{{ .Values.image.repository }}");
    }
}
