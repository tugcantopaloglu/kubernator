using Kubernator.Core.Detection.DotNet;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Core.Tests.Detection;

public sealed class DotNetDetectorTests
{
    private readonly DotNetDetector detector = new(NullLogger<DotNetDetector>.Instance);

    [Fact]
    public async Task Detects_aspnet_publish_with_high_confidence()
    {
        using var pub = PublishFixtures.AspNetCorePublish();

        var result = await detector.DetectAsync(pub.Path);

        result.Kind.Should().Be(AppKind.DotNet);
        result.Flavor.Should().Be(AppFlavor.DotNetAspNetCore);
        result.Confidence.Should().BeGreaterOrEqualTo(0.9);
        result.Signals.Should().Contain(s => s.Contains("MyWebApp.deps.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detects_console_publish()
    {
        using var pub = PublishFixtures.ConsolePublish();

        var result = await detector.DetectAsync(pub.Path);

        result.Kind.Should().Be(AppKind.DotNet);
        result.Flavor.Should().Be(AppFlavor.DotNetConsole);
        result.Confidence.Should().BeGreaterOrEqualTo(0.9);
    }

    [Fact]
    public async Task Detects_source_tree_with_lower_confidence()
    {
        using var src = PublishFixtures.SourceTree();

        var result = await detector.DetectAsync(src.Path);

        result.Kind.Should().Be(AppKind.DotNet);
        result.Confidence.Should().BeInRange(0.4, 0.7);
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Returns_none_for_empty_directory()
    {
        using var empty = TempPublishOutput.Create();

        var result = await detector.DetectAsync(empty.Path);

        result.Kind.Should().Be(AppKind.Unknown);
        result.Confidence.Should().Be(0);
    }
}
