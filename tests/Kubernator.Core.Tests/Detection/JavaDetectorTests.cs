using System.IO.Compression;
using System.Text;
using Kubernator.Core.Detection.Java;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Core.Tests.Detection;

public sealed class JavaDetectorTests
{
    private readonly JavaDetector detector = new(NullLogger<JavaDetector>.Instance);

    [Fact]
    public async Task Detects_spring_boot_jar()
    {
        using var t = TempPublishOutput.Create();
        var jar = Path.Combine(t.Path, "demo-1.0.0.jar");
        BuildJar(jar, manifest:
            "Manifest-Version: 1.0\n" +
            "Main-Class: org.springframework.boot.loader.JarLauncher\n" +
            "Start-Class: com.example.Demo\n" +
            "Spring-Boot-Version: 3.2.0\n" +
            "Implementation-Title: demo\n" +
            "Implementation-Version: 1.0.0\n",
            extraEntries: new Dictionary<string, string>
            {
                ["BOOT-INF/classes/application.properties"] = "server.port=9090\n",
                ["BOOT-INF/classes/Empty.class"] = ""
            });

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.Java);
        result.Flavor.Should().Be(AppFlavor.JavaSpringBoot);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.9);
    }

    [Fact]
    public async Task Detects_plain_runnable_jar()
    {
        using var t = TempPublishOutput.Create();
        var jar = Path.Combine(t.Path, "tool.jar");
        BuildJar(jar,
            manifest: "Manifest-Version: 1.0\nMain-Class: com.example.Tool\n",
            extraEntries: new Dictionary<string, string> { ["com/example/Tool.class"] = "" });

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.Java);
        result.Flavor.Should().Be(AppFlavor.JavaGeneric);
    }

    [Fact]
    public async Task Returns_low_confidence_for_pom_only()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("pom.xml", "<project/>");
        var result = await detector.DetectAsync(t.Path);
        result.Confidence.Should().BeLessThan(0.7);
        result.Kind.Should().Be(AppKind.Java);
    }

    private static void BuildJar(string path, string manifest, IDictionary<string, string> extraEntries)
    {
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry("META-INF/MANIFEST.MF");
        using (var s = manifestEntry.Open())
        {
            var bytes = Encoding.UTF8.GetBytes(manifest);
            s.Write(bytes, 0, bytes.Length);
        }

        foreach (var (name, content) in extraEntries)
        {
            var entry = archive.CreateEntry(name);
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }
}
