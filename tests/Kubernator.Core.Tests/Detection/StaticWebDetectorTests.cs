using Kubernator.Core.Detection.Static;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;

namespace Kubernator.Core.Tests.Detection;

public sealed class StaticWebDetectorTests
{
    private readonly StaticWebDetector detector = new();

    [Fact]
    public async Task Detects_root_index_html()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("index.html", "<html></html>");
        t.WriteFile("assets/app.js", "console.log(1)");

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.StaticWeb);
        result.Flavor.Should().Be(AppFlavor.StaticSpa);
    }

    [Fact]
    public async Task Detects_dist_folder()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("dist/index.html", "<html></html>");
        t.WriteFile("dist/static/app.js", "");

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.StaticWeb);
        result.SourcePath.Should().EndWith("dist");
    }

    [Fact]
    public async Task Skips_when_node_modules_present()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("index.html", "");
        t.WriteFile("package.json", "{}");
        t.WriteFile("node_modules/.placeholder", "");

        var result = await detector.DetectAsync(t.Path);
        result.Kind.Should().Be(AppKind.Unknown);
    }
}
