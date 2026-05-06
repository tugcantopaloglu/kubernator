using Kubernator.Core.AirGapped;
using Kubernator.Core.Tests.Packaging;

namespace Kubernator.Core.Tests.AirGapped;

public class ImageBundleServiceTests : IDisposable
{
    private readonly string scratch;

    public ImageBundleServiceTests()
    {
        scratch = Path.Combine(Path.GetTempPath(), "kubernator-imgtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(scratch, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PullAsync_PullsAndSavesEachImage()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();

        var result = await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27", "redis:7.4"],
            OutputDirectory = scratch
        }, engine);

        result.Manifest.Images.Should().HaveCount(2);
        engine.Pulled.Select(p => p.Reference).Should().BeEquivalentTo(["nginx:1.27", "redis:7.4"]);
        engine.SavedImages.Should().BeEquivalentTo(["nginx:1.27", "redis:7.4"]);
        File.Exists(Path.Combine(scratch, "images.manifest.json")).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_SkipsPullWhenImageAlreadyPresentUnlessForce()
    {
        var engine = new FakeContainerEngine();
        engine.Register("nginx:1.27");
        var sut = new ImageBundleService();

        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27"],
            OutputDirectory = scratch
        }, engine);

        engine.Pulled.Should().BeEmpty();
        engine.SavedImages.Should().ContainSingle().Which.Should().Be("nginx:1.27");
    }

    [Fact]
    public async Task PullAsync_ForcePull_PullsEvenIfPresent()
    {
        var engine = new FakeContainerEngine();
        engine.Register("nginx:1.27");
        var sut = new ImageBundleService();

        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27"],
            OutputDirectory = scratch,
            ForcePull = true
        }, engine);

        engine.Pulled.Should().ContainSingle();
    }

    [Fact]
    public async Task PullAsync_NormalizesReferenceWithoutTag()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();

        var result = await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx"],
            OutputDirectory = scratch
        }, engine);

        result.Manifest.Images.Single().Reference.Should().Be("nginx:latest");
    }

    [Fact]
    public async Task PullAsync_EmptyReferences_Throws()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();

        var act = () => sut.PullAsync(new ImageBundleOptions
        {
            References = [],
            OutputDirectory = scratch
        }, engine);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RehostAsync_LoadsAndPushesEachImage()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();
        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27", "ghcr.io/example/app:v1"],
            OutputDirectory = scratch
        }, engine);

        var result = await sut.RehostAsync(new ImageRehostOptions
        {
            BundleDirectory = scratch,
            TargetRegistry = "registry.airgap.local:5000"
        }, engine);

        result.Ok.Should().BeTrue();
        result.Pushed.Should().HaveCount(2);
        result.Pushed.Select(p => p.TargetReference).Should().Contain([
            "registry.airgap.local:5000/nginx:1.27",
            "registry.airgap.local:5000/example/app:v1"
        ]);
        engine.Loaded.Should().HaveCount(2);
        engine.Tagged.Should().HaveCount(2);
        engine.Pushed.Should().HaveCount(2);
    }

    [Fact]
    public async Task RehostAsync_AppliesNamespacePath()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();
        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27"],
            OutputDirectory = scratch
        }, engine);

        var result = await sut.RehostAsync(new ImageRehostOptions
        {
            BundleDirectory = scratch,
            TargetRegistry = "registry.airgap.local:5000",
            TargetNamespace = "infra/mirror"
        }, engine);

        result.Pushed.Single().TargetReference.Should().Be("registry.airgap.local:5000/infra/mirror/nginx:1.27");
    }

    [Fact]
    public async Task RehostAsync_RewritesManifestImages()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();
        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27"],
            OutputDirectory = scratch
        }, engine);

        var manifestsDir = Path.Combine(scratch, "k8s");
        Directory.CreateDirectory(manifestsDir);
        var deployment = """
            apiVersion: apps/v1
            kind: Deployment
            spec:
              template:
                spec:
                  containers:
                    - name: app
                      image: nginx:1.27
            """;
        var deployPath = Path.Combine(manifestsDir, "deployment.yaml");
        await File.WriteAllTextAsync(deployPath, deployment);

        var result = await sut.RehostAsync(new ImageRehostOptions
        {
            BundleDirectory = scratch,
            TargetRegistry = "registry.airgap.local:5000",
            ManifestsDirectory = manifestsDir
        }, engine);

        result.RewrittenManifestFiles.Should().ContainSingle().Which.Should().Be(deployPath);
        var rewritten = await File.ReadAllTextAsync(deployPath);
        rewritten.Should().Contain("image: registry.airgap.local:5000/nginx:1.27");
        rewritten.Should().NotContain("image: nginx:1.27");
    }

    [Fact]
    public async Task RehostAsync_NoLoad_SkipsLoad()
    {
        var engine = new FakeContainerEngine();
        var sut = new ImageBundleService();
        await sut.PullAsync(new ImageBundleOptions
        {
            References = ["nginx:1.27"],
            OutputDirectory = scratch
        }, engine);

        await sut.RehostAsync(new ImageRehostOptions
        {
            BundleDirectory = scratch,
            TargetRegistry = "registry.airgap.local:5000",
            LoadBeforePush = false
        }, engine);

        engine.Loaded.Should().BeEmpty();
        engine.Pushed.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReadManifestAsync_ReturnsNullWhenMissing()
    {
        var sut = new ImageBundleService();
        var result = await sut.ReadManifestAsync(scratch);
        result.Should().BeNull();
    }
}
