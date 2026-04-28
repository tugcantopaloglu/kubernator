using Kubernator.Core.Abstractions;
using Kubernator.Core.Detection.Java;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Analysis.Java;

public sealed class JavaAnalyzer : IAppAnalyzer
{
    private readonly ILogger<JavaAnalyzer> logger;

    public JavaAnalyzer(ILogger<JavaAnalyzer> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.Java;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = detection.SourcePath;
        var jars = Directory.EnumerateFiles(path, "*.jar", SearchOption.TopDirectoryOnly).ToArray();
        var wars = Directory.EnumerateFiles(path, "*.war", SearchOption.TopDirectoryOnly).ToArray();
        var artefacts = jars.Concat(wars).OrderByDescending(p => new FileInfo(p).Length).ToArray();
        if (artefacts.Length == 0)
        {
            return Task.FromResult(MinimalDescriptor(detection));
        }

        var primary = artefacts[0];
        var warnings = new List<string>(detection.Warnings);
        JarMetadata? meta = null;
        try
        {
            meta = JarReader.Read(primary);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JAR read failed");
            warnings.Add($"jar parse failed: {ex.Message}");
        }

        var ports = new List<int>();
        var listensHttp = false;
        var requiresIngress = false;
        if (meta?.ApplicationProperties.TryGetValue("server.port", out var portRaw) == true &&
            int.TryParse(portRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port))
        {
            ports.Add(port);
            listensHttp = true;
            requiresIngress = true;
        }
        else if (detection.Flavor is AppFlavor.JavaSpringBoot or AppFlavor.JavaQuarkus)
        {
            ports.Add(8080);
            listensHttp = true;
            requiresIngress = true;
        }

        var entryClass = meta?.StartClass ?? meta?.MainClass;
        var entry = new EntryPoint
        {
            Path = primary,
            AssemblyName = meta?.ImplementationTitle ?? Path.GetFileNameWithoutExtension(primary),
            StartupCommand = "java",
            Arguments = ["-jar", Path.GetFileName(primary)]
        };

        var runtime = new RuntimeInfo
        {
            Name = "Java",
            Version = meta?.ImplementationVersion,
            Tfm = null,
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.Unknown,
            PublishMode = PublishMode.FrameworkDependent,
            FrameworkReferences = entryClass is null ? [] : [entryClass]
        };

        var network = new NetworkInfo
        {
            Ports = ports,
            ListensHttp = listensHttp,
            RequiresIngress = requiresIngress
        };

        var deps = new DependencyInfo
        {
            Managed = [],
            Native = [],
            RequiresIcu = true,
            RequiresTimezone = true,
            RequiresGdiPlus = false
        };

        var envHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JAVA_TOOL_OPTIONS"] = "-XX:+UseContainerSupport -XX:MaxRAMPercentage=75.0",
            ["SPRING_PROFILES_ACTIVE"] = "production"
        };
        if (listensHttp && ports.Count > 0)
        {
            envHints["SERVER_PORT"] = ports[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Task.FromResult(new AppDescriptor
        {
            SourcePath = path,
            Kind = AppKind.Java,
            Flavor = detection.Flavor,
            Runtime = runtime,
            Network = network,
            Dependencies = deps,
            EntryPoint = entry,
            EnvironmentHints = envHints,
            Warnings = warnings,
            DetectionConfidence = detection.Confidence
        });
    }

    private static AppDescriptor MinimalDescriptor(DetectionResult detection) => new()
    {
        SourcePath = detection.SourcePath,
        Kind = AppKind.Java,
        Flavor = detection.Flavor,
        Runtime = new RuntimeInfo { Name = "Java" },
        Network = new NetworkInfo(),
        Dependencies = new DependencyInfo(),
        EntryPoint = null,
        EnvironmentHints = new Dictionary<string, string>(),
        Warnings = [.. detection.Warnings, "no jar/war found; bundle a runnable artefact first"],
        DetectionConfidence = detection.Confidence
    };
}
