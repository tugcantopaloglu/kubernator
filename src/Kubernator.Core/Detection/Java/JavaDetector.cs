using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Detection.Java;

public sealed class JavaDetector : IAppDetector
{
    private readonly ILogger<JavaDetector> logger;

    public JavaDetector(ILogger<JavaDetector> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.Java;

    public Task<DetectionResult> DetectAsync(string path, CancellationToken ct = default)
    {
        var resolved = Path.GetFullPath(path);
        if (File.Exists(resolved))
        {
            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir))
            {
                resolved = dir;
            }
        }
        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var jars = Directory.EnumerateFiles(resolved, "*.jar", SearchOption.TopDirectoryOnly).ToArray();
        var wars = Directory.EnumerateFiles(resolved, "*.war", SearchOption.TopDirectoryOnly).ToArray();
        var artefacts = jars.Concat(wars).ToArray();
        if (artefacts.Length == 0)
        {
            return Task.FromResult(DetectSourceLayout(resolved));
        }

        var primary = SelectPrimary(artefacts);
        var signals = new List<string> { Path.GetFileName(primary) };
        var warnings = new List<string>();
        var flavor = AppFlavor.JavaGeneric;
        var confidence = 0.8;

        try
        {
            var meta = JarReader.Read(primary);
            if (meta.IsSpringBoot)
            {
                flavor = AppFlavor.JavaSpringBoot;
                signals.Add("framework: Spring Boot");
                confidence = 0.95;
            }
            else if (meta.IsQuarkus)
            {
                flavor = AppFlavor.JavaQuarkus;
                signals.Add("framework: Quarkus");
                confidence = 0.95;
            }
            else if (!string.IsNullOrEmpty(meta.MainClass))
            {
                signals.Add($"main-class: {meta.MainClass}");
                confidence = 0.9;
            }

            if (meta.IsWar)
            {
                signals.Add(".war archive");
                warnings.Add("WAR archives require an external servlet container");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect jar {Jar}", primary);
            warnings.Add($"jar inspection failed: {ex.Message}");
        }

        if (artefacts.Length > 1)
        {
            warnings.Add($"multiple jar/war files found; using {Path.GetFileName(primary)}");
        }

        return Task.FromResult(new DetectionResult
        {
            SourcePath = resolved,
            Kind = AppKind.Java,
            Flavor = flavor,
            Confidence = confidence,
            Signals = signals,
            Warnings = warnings
        });
    }

    private static string SelectPrimary(IReadOnlyList<string> artefacts)
    {
        return artefacts
            .OrderByDescending(p => new FileInfo(p).Length)
            .First();
    }

    private static DetectionResult DetectSourceLayout(string root)
    {
        if (File.Exists(Path.Combine(root, "pom.xml")))
        {
            return new DetectionResult
            {
                SourcePath = root,
                Kind = AppKind.Java,
                Flavor = AppFlavor.JavaGeneric,
                Confidence = 0.5,
                Signals = ["pom.xml"],
                Warnings = ["maven source tree detected; build a runnable jar before bundling"]
            };
        }
        if (File.Exists(Path.Combine(root, "build.gradle")) || File.Exists(Path.Combine(root, "build.gradle.kts")))
        {
            return new DetectionResult
            {
                SourcePath = root,
                Kind = AppKind.Java,
                Flavor = AppFlavor.JavaGeneric,
                Confidence = 0.5,
                Signals = ["gradle build script"],
                Warnings = ["gradle source tree detected; build a runnable jar before bundling"]
            };
        }
        return DetectionResult.None(root);
    }
}
