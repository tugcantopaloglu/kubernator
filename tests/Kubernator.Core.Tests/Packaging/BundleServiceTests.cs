using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Kubernator.Core.Packaging;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.Packaging;

public sealed class BundleServiceTests
{
    private readonly StrategySelector strategy = new();
    private readonly BundleService service = new();

    [Fact]
    public async Task Create_emits_kubpack_with_image_manifests_sbom_scripts()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);

        var output = Path.Combine(temp.Path, "myapp.kubpack");
        var scratch = Path.Combine(temp.Path, "scratch");

        var result = await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = output,
            ScratchDirectory = scratch,
            KubernetesNamespace = "demo",
            Replicas = 2
        }, engine);

        File.Exists(result.BundlePath).Should().BeTrue();
        result.BundleSizeBytes.Should().BeGreaterThan(0);
        result.Manifest.Images.Should().HaveCount(1);
        result.Manifest.KubernetesNamespace.Should().Be("demo");
        engine.SavedImages.Should().Contain(plan.FullImageReference);

        var entries = await ListBundleEntriesAsync(output);
        entries.Should().Contain("manifest.json");
        entries.Should().Contain("manifest.sha256");
        entries.Should().Contain(e => e.StartsWith("images/", StringComparison.Ordinal) && e.EndsWith(".tar", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("manifests/deployment.yaml", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("manifests/service.yaml", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("manifests/networkpolicy.yaml", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("scripts/install.sh", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("scripts/install.ps1", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("scripts/verify.sh", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("sbom/", StringComparison.Ordinal) && e.EndsWith(".cyclonedx.json", StringComparison.Ordinal));
        entries.Should().Contain(e => e.StartsWith("sbom/", StringComparison.Ordinal) && e.EndsWith(".spdx.json", StringComparison.Ordinal));
        entries.Should().Contain("install.sh");
        entries.Should().Contain("install.ps1");
    }

    [Fact]
    public async Task Verify_passes_for_a_freshly_created_bundle()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);

        var output = Path.Combine(temp.Path, "ok.kubpack");
        await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = output,
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        var verify = await service.VerifyAsync(output);

        verify.Ok.Should().BeTrue();
        verify.Errors.Should().BeEmpty();
        verify.Manifest.Should().NotBeNull();
        verify.Manifest!.Tool.Should().Be("kubernator");
    }

    [Fact]
    public async Task Verify_fails_when_a_file_inside_bundle_is_tampered()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);

        var bundlePath = Path.Combine(temp.Path, "tampered.kubpack");
        await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        var tamperedDir = Path.Combine(temp.Path, "tamperdir");
        Directory.CreateDirectory(tamperedDir);
        await using (var fs = File.OpenRead(bundlePath))
        await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gz, tamperedDir, overwriteFiles: true);
        }
        var deploymentPath = Path.Combine(tamperedDir, "manifests", "deployment.yaml");
        await File.AppendAllTextAsync(deploymentPath, "tamper\n");

        var rebuilt = Path.Combine(temp.Path, "rebuilt.kubpack");
        await using (var fs = File.Create(rebuilt))
        await using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            await TarFile.CreateFromDirectoryAsync(tamperedDir, gz, includeBaseDirectory: false);
        }

        var verify = await service.VerifyAsync(rebuilt);

        verify.Ok.Should().BeFalse();
        verify.Errors.Should().Contain(e => e.Contains("deployment.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_fails_when_image_tar_is_tampered()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);

        var bundlePath = Path.Combine(temp.Path, "image-tampered.kubpack");
        await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        var dir = Path.Combine(temp.Path, "image-tamperdir");
        Directory.CreateDirectory(dir);
        await using (var fs = File.OpenRead(bundlePath))
        await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gz, dir, overwriteFiles: true);
        }
        var imageTar = Directory.GetFiles(Path.Combine(dir, "images"), "*.tar").First();
        await File.AppendAllBytesAsync(imageTar, [0xDE, 0xAD, 0xBE, 0xEF]);

        var rebuilt = Path.Combine(temp.Path, "image-rebuilt.kubpack");
        await using (var fs = File.Create(rebuilt))
        await using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            await TarFile.CreateFromDirectoryAsync(dir, gz, includeBaseDirectory: false);
        }

        var verify = await service.VerifyAsync(rebuilt);

        verify.Ok.Should().BeFalse();
        verify.Errors.Should().Contain(e => e.Contains("images/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Builds_image_when_missing()
    {
        using var temp = TempPublishOutput.Create();
        File.WriteAllText(Path.Combine(temp.Path, "MyWebApp.dll"), string.Empty);
        var app = SampleApp.AspNet() with { SourcePath = temp.Path };
        var plan = strategy.Plan(app);

        var engine = new FakeContainerEngine();
        engine.EnableSimulateMissingThenBuild();

        var bundlePath = Path.Combine(temp.Path, "auto-build.kubpack");
        await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = Path.Combine(temp.Path, "scratch"),
            BuildIfMissing = true
        }, engine);

        engine.Builds.Should().HaveCount(1);
        engine.SavedImages.Should().Contain(plan.FullImageReference);
        File.Exists(bundlePath).Should().BeTrue();
    }

    [Fact]
    public async Task Multi_arch_bundle_emits_one_image_tar_per_platform()
    {
        using var temp = TempPublishOutput.Create();
        File.WriteAllText(Path.Combine(temp.Path, "MyWebApp.dll"), string.Empty);
        var app = SampleApp.AspNet() with { SourcePath = temp.Path };
        var plan = strategy.Plan(app, new StrategyOptions
        {
            Platforms = ["linux/amd64", "linux/arm64"]
        });
        var engine = new FakeContainerEngine { SupportsMultiPlatform = true };

        var bundlePath = Path.Combine(temp.Path, "multi.kubpack");
        var result = await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        result.Manifest.Images.Should().HaveCount(2);
        result.Manifest.Images.Select(i => i.Platform).Should().BeEquivalentTo(["linux/amd64", "linux/arm64"]);
        engine.SavedPerPlatform.Select(p => p.Platform).Should().BeEquivalentTo(["linux/amd64", "linux/arm64"]);

        var entries = await ListBundleEntriesAsync(bundlePath);
        entries.Should().Contain(e => e.EndsWith("-amd64.tar", StringComparison.Ordinal));
        entries.Should().Contain(e => e.EndsWith("-arm64.tar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Multi_arch_request_against_single_platform_engine_throws()
    {
        using var temp = TempPublishOutput.Create();
        File.WriteAllText(Path.Combine(temp.Path, "MyWebApp.dll"), string.Empty);
        var app = SampleApp.AspNet() with { SourcePath = temp.Path };
        var plan = strategy.Plan(app, new StrategyOptions
        {
            Platforms = ["linux/amd64", "linux/arm64"]
        });
        var engine = new FakeContainerEngine();

        var act = () => service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = Path.Combine(temp.Path, "x.kubpack"),
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not support*");
    }

    [Fact]
    public async Task Same_input_with_source_date_epoch_yields_identical_bundle_hash()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);
        var epoch = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        async Task<string> BuildAsync(string suffix)
        {
            var bundle = Path.Combine(temp.Path, $"repro-{suffix}.kubpack");
            var scratch = Path.Combine(temp.Path, $"scratch-{suffix}");
            var result = await service.CreateAsync(plan, new BundleOptions
            {
                OutputBundlePath = bundle,
                ScratchDirectory = scratch,
                SourceDateEpoch = epoch
            }, engine);
            return result.BundleSha256;
        }

        var first = await BuildAsync("a");
        var second = await BuildAsync("b");

        second.Should().Be(first, because: "with a fixed SourceDateEpoch the bundle must be byte-identical");
    }

    [Fact]
    public async Task Manifest_json_lists_all_files_with_hashes()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());
        var engine = new FakeContainerEngine();
        engine.Register(plan.FullImageReference);

        var bundlePath = Path.Combine(temp.Path, "manifest-test.kubpack");
        var result = await service.CreateAsync(plan, new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = Path.Combine(temp.Path, "scratch")
        }, engine);

        var manifestText = JsonSerializer.Serialize(result.Manifest, JsonOpts);
        manifestText.Should().Contain("\"SchemaVersion\": \"1.0\"");
        result.Manifest.Files.Should().NotBeEmpty();
        result.Manifest.Files.Should().AllSatisfy(f =>
        {
            f.Sha256.Should().HaveLength(64);
            f.SizeBytes.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static async Task<List<string>> ListBundleEntriesAsync(string bundlePath)
    {
        var names = new List<string>();
        await using var fs = File.OpenRead(bundlePath);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await using var tar = new TarReader(gz);
        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync()) is not null)
        {
            names.Add(entry.Name.Replace('\\', '/'));
        }
        return names;
    }
}
