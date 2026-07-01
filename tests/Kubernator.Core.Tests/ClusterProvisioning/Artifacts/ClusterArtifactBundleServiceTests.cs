using System.Formats.Tar;
using System.IO.Compression;
using Kubernator.Core.AirGapped;
using Kubernator.Core.ClusterProvisioning.Artifacts;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.Containers;
using NSubstitute;

namespace Kubernator.Core.Tests.ClusterProvisioning.Artifacts;

public sealed class ClusterArtifactBundleServiceTests : IDisposable
{
    private readonly string tempDir;

    public ClusterArtifactBundleServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"artifactplan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Rke2_plan_includes_artifact_images_and_install_script_per_arch()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Architectures = ["amd64", "arm64"]
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        plan.Should().ContainSingle(i => i.Kind == "install-script" && i.RelativePath == "install.sh");
        plan.Should().Contain(i => i.Kind == "rke2-artifact" && i.Arch == "amd64" && i.RelativePath == "artifacts/amd64/rke2.linux-amd64.tar.gz");
        plan.Should().Contain(i => i.Kind == "rke2-images" && i.Arch == "arm64" && i.RelativePath == "artifacts/arm64/rke2-images.linux-arm64.tar.zst");
        plan.Should().NotContain(i => i.Kind == "selinux-policy");
    }

    [Fact]
    public void K3s_plan_uses_arm64_specific_binary_name()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.K3s,
            Version = "v1.30.4+k3s1",
            Architectures = ["amd64", "arm64"]
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        plan.Should().Contain(i => i.Kind == "k3s-binary" && i.Arch == "amd64" && i.RelativePath == "artifacts/amd64/k3s");
        plan.Should().Contain(i => i.Kind == "k3s-binary" && i.Arch == "arm64" && i.RelativePath == "artifacts/arm64/k3s-arm64");
    }

    [Fact]
    public void Plan_includes_selinux_policy_when_requested_and_marks_it_optional()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Architectures = ["amd64"],
            IncludeSelinuxPolicy = true
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        var selinux = plan.Should().ContainSingle(i => i.Kind == "selinux-policy").Which;
        selinux.Required.Should().BeFalse();
    }

    [Fact]
    public void Plan_includes_kubectl_helm_k9s_when_requested_with_kube_version_stripped_of_distro_suffix()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Architectures = ["amd64"],
            IncludeKubectl = true,
            IncludeHelm = true,
            IncludeK9s = true,
            HelmVersion = "v3.16.2",
            K9sVersion = "v0.32.5"
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        plan.Should().Contain(i => i.Kind == "kubectl" && i.Url == "https://dl.k8s.io/release/v1.30.4/bin/linux/amd64/kubectl");
        plan.Should().Contain(i => i.Kind == "helm" && i.Url.Contains("v3.16.2"));
        plan.Should().Contain(i => i.Kind == "k9s" && i.Url.Contains("v0.32.5"));
    }

    [Fact]
    public void Kubeadm_plan_includes_binaries_containerd_runc_cni_plugins_and_both_cni_manifests()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.KubeadmNative,
            Version = "v1.30.4",
            Architectures = ["amd64", "arm64"]
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        plan.Should().Contain(i => i.Kind == "kubeadm-binary" && i.Arch == "amd64" && i.RelativePath == "artifacts/amd64/kubeadm");
        plan.Should().Contain(i => i.Kind == "kubelet-binary" && i.Arch == "amd64" && i.RelativePath == "artifacts/amd64/kubelet");
        plan.Should().Contain(i => i.Kind == "kubectl-binary" && i.Arch == "arm64" && i.RelativePath == "artifacts/arm64/kubectl");
        plan.Should().Contain(i => i.Kind == "containerd" && i.Arch == "amd64");
        plan.Should().Contain(i => i.Kind == "runc" && i.Arch == "amd64");
        plan.Should().Contain(i => i.Kind == "cni-plugins" && i.Arch == "amd64");
        plan.Should().ContainSingle(i => i.Kind == "cni-manifest" && i.RelativePath == "cni/flannel.yaml");
        plan.Should().ContainSingle(i => i.Kind == "cni-manifest" && i.RelativePath == "cni/calico.yaml");
    }

    [Fact]
    public void Kubeadm_plan_kubeadm_binary_url_uses_kube_version_stripped_of_any_suffix()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.KubeadmNative,
            Version = "v1.30.4",
            Architectures = ["amd64"]
        };

        var plan = ClusterArtifactBundleService.BuildDownloadPlan(options);

        plan.Should().Contain(i => i.Kind == "kubeadm-binary" && i.Url == "https://dl.k8s.io/release/v1.30.4/bin/linux/amd64/kubeadm");
    }

    [Fact]
    public async Task PullAsync_for_kubeadm_pulls_container_images_via_image_bundle_service_per_arch()
    {
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = tempDir,
            Distro = DistroKind.KubeadmNative,
            Version = "v1.30.4",
            Architectures = ["amd64"],
            IncludeKubectl = false
        };

        var engine = Substitute.For<IContainerEngine>();
        var engineProvider = Substitute.For<IContainerEngineProvider>();
        engineProvider.ResolveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(engine));

        var imageBundleService = Substitute.For<IImageBundleService>();
        imageBundleService
            .PullAsync(Arg.Any<ImageBundleOptions>(), Arg.Any<IContainerEngine>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var opts = callInfo.Arg<ImageBundleOptions>();
                Directory.CreateDirectory(opts.OutputDirectory);
                return Task.FromResult(new ImageBundleResult
                {
                    OutputDirectory = opts.OutputDirectory,
                    ManifestPath = Path.Combine(opts.OutputDirectory, "images.manifest.json"),
                    Manifest = new ImageBundleManifest
                    {
                        SchemaVersion = "1.0",
                        Tool = "kubernator",
                        ToolVersion = "0.0.0",
                        CreatedAt = DateTimeOffset.UnixEpoch,
                        Images = opts.References.Select(reference => new ImageBundleEntry
                        {
                            Reference = reference,
                            TarRelativePath = "img.tar",
                            SizeBytes = 123,
                            Sha256 = "deadbeef",
                            ImageId = "sha256:abc",
                            Platform = opts.Platform
                        }).ToArray()
                    }
                });
            });

        var sut = new ClusterArtifactBundleService(new HttpClient(new FakeHttpMessageHandler()), imageBundleService, engineProvider);

        var manifest = await sut.PullAsync(options);

        manifest.Entries.Should().Contain(e => e.Kind == "container-image" && e.Arch == "amd64" && e.RelativePath == "images/amd64/img.tar");
        await imageBundleService.Received(1).PullAsync(
            Arg.Is<ImageBundleOptions>(o => o.Platform == "linux/amd64" && o.References.Count == 7),
            engine, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(".yaml", StringComparison.Ordinal) ? "fake-manifest" : "fake-content";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }

    [Fact]
    public async Task PackAsync_creates_tar_gz_with_nested_files_and_excludes_the_output_archive_itself()
    {
        var bundleDir = Path.Combine(tempDir, "bundle");
        Directory.CreateDirectory(Path.Combine(bundleDir, "artifacts", "amd64"));
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "install.sh"), "echo hi");
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "artifacts", "amd64", "rke2.linux-amd64.tar.gz"), "fake-tar-content");

        var archivePath = Path.Combine(bundleDir, "bundle.tar.gz");
        var sut = new ClusterArtifactBundleService(new HttpClient(), Substitute.For<IImageBundleService>(), Substitute.For<IContainerEngineProvider>());

        var returned = await sut.PackAsync(bundleDir, archivePath);

        returned.Should().Be(archivePath);
        File.Exists(archivePath).Should().BeTrue();

        var entryNames = new List<string>();
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync()) is not null)
        {
            entryNames.Add(entry.Name);
        }

        entryNames.Should().Contain("install.sh");
        entryNames.Should().Contain("artifacts/amd64/rke2.linux-amd64.tar.gz");
        entryNames.Should().NotContain("bundle.tar.gz");
    }
}
