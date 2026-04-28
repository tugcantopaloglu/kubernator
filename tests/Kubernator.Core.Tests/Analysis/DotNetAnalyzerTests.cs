using Kubernator.Core.Analysis.DotNet;
using Kubernator.Core.Detection.DotNet;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Core.Tests.Analysis;

public sealed class DotNetAnalyzerTests
{
    private readonly DotNetDetector detector = new(NullLogger<DotNetDetector>.Instance);
    private readonly DotNetAnalyzer analyzer = new(NullLogger<DotNetAnalyzer>.Instance);

    [Fact]
    public async Task Analyzes_aspnet_publish_with_ports_and_frameworks()
    {
        using var pub = PublishFixtures.AspNetCorePublish();

        var detection = await detector.DetectAsync(pub.Path);
        var descriptor = await analyzer.AnalyzeAsync(detection);

        descriptor.Kind.Should().Be(AppKind.DotNet);
        descriptor.Flavor.Should().Be(AppFlavor.DotNetAspNetCore);
        descriptor.Runtime.Tfm.Should().Be("net10.0");
        descriptor.Runtime.Version.Should().Be("10.0.0");
        descriptor.Runtime.RuntimeIdentifier.Should().Be("linux-x64");
        descriptor.Runtime.TargetOs.Should().Be(TargetOs.Linux);
        descriptor.Runtime.TargetArch.Should().Be(TargetArchitecture.X64);
        descriptor.Runtime.FrameworkReferences.Should().Contain("Microsoft.AspNetCore.App");

        descriptor.Network.Ports.Should().Contain([5080, 5443, 8080]);
        descriptor.Network.ListensHttp.Should().BeTrue();
        descriptor.Network.ListensHttps.Should().BeTrue();
        descriptor.Network.RequiresIngress.Should().BeTrue();

        descriptor.Dependencies.RequiresIcu.Should().BeTrue();
        descriptor.EnvironmentHints.Should().ContainKey("ASPNETCORE_ENVIRONMENT");
        descriptor.EnvironmentHints.Should().ContainKey("DOTNET_RUNNING_IN_CONTAINER");
    }

    [Fact]
    public async Task Analyzes_console_publish_with_no_ports()
    {
        using var pub = PublishFixtures.ConsolePublish();

        var detection = await detector.DetectAsync(pub.Path);
        var descriptor = await analyzer.AnalyzeAsync(detection);

        descriptor.Flavor.Should().Be(AppFlavor.DotNetConsole);
        descriptor.Network.Ports.Should().BeEmpty();
        descriptor.Network.RequiresIngress.Should().BeFalse();
    }

    [Fact]
    public async Task Defaults_aspnet_port_to_8080_when_none_configured()
    {
        using var pub = TempPublishOutput.Create();
        var template = PublishFixtures.AspNetCorePublish();
        try
        {
            foreach (var f in Directory.EnumerateFiles(template.Path))
            {
                if (f.EndsWith("appsettings.json", StringComparison.Ordinal))
                {
                    continue;
                }
                pub.CopyFile(f, Path.GetFileName(f));
            }
        }
        finally
        {
            template.Dispose();
        }

        var detection = await detector.DetectAsync(pub.Path);
        var descriptor = await analyzer.AnalyzeAsync(detection);

        descriptor.Flavor.Should().Be(AppFlavor.DotNetAspNetCore);
        descriptor.Network.Ports.Should().Contain(8080);
        descriptor.Warnings.Should().Contain(w => w.Contains("8080", StringComparison.Ordinal));
    }
}
