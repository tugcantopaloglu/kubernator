using Kubernator.Core.Detection.Go;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;

namespace Kubernator.Core.Tests.Detection;

public sealed class GoDetectorTests
{
    private readonly GoDetector detector = new();

    [Fact]
    public async Task Detects_go_mod_source_tree()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("go.mod", "module example.com/foo\n\ngo 1.22\n");

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.Go);
        result.Flavor.Should().Be(AppFlavor.GoBinary);
    }

    [Fact]
    public async Task Detects_elf_binary_heuristically()
    {
        using var t = TempPublishOutput.Create();
        var binPath = Path.Combine(t.Path, "myapp");
        await File.WriteAllBytesAsync(binPath, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01]);

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.Go);
        result.Confidence.Should().BeGreaterThan(0);
    }
}
