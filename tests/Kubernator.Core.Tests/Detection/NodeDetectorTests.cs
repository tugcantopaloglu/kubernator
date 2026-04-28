using Kubernator.Core.Detection.Node;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Core.Tests.Detection;

public sealed class NodeDetectorTests
{
    private readonly NodeDetector detector = new(NullLogger<NodeDetector>.Instance);

    [Fact]
    public async Task Detects_express_app()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("package.json", """
        {
          "name": "demo",
          "version": "1.0.0",
          "main": "index.js",
          "scripts": { "start": "node index.js" },
          "dependencies": { "express": "^4.19.2" },
          "engines": { "node": ">=20" }
        }
        """);
        t.WriteFile("index.js", "console.log('hi')");

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.NodeJs);
        result.Flavor.Should().Be(AppFlavor.NodeExpress);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public async Task Detects_nextjs_when_dependency_present()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("package.json", """
        { "name": "site", "version": "0.0.1", "dependencies": { "next": "^14.0.0" } }
        """);

        var result = await detector.DetectAsync(t.Path);

        result.Flavor.Should().Be(AppFlavor.NodeNext);
    }

    [Fact]
    public async Task Returns_none_when_no_package_json()
    {
        using var t = TempPublishOutput.Create();
        var result = await detector.DetectAsync(t.Path);
        result.Confidence.Should().Be(0);
    }
}
