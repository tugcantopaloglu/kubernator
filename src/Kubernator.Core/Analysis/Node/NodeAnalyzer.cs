using Kubernator.Core.Abstractions;
using Kubernator.Core.Detection.Node;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Analysis.Node;

public sealed class NodeAnalyzer : IAppAnalyzer
{
    private readonly ILogger<NodeAnalyzer> logger;

    public NodeAnalyzer(ILogger<NodeAnalyzer> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.NodeJs;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = detection.SourcePath;
        var packageJsonPath = Path.Combine(path, "package.json");

        PackageJsonModel pkg;
        try
        {
            pkg = PackageJsonReader.Read(packageJsonPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "package.json read failed");
            pkg = new PackageJsonModel();
        }

        var (defaultPort, listensHttp, requiresIngress) = ResolveNetwork(detection.Flavor);
        var ports = new List<int>();
        if (listensHttp)
        {
            ports.Add(defaultPort);
        }

        var warnings = new List<string>(detection.Warnings);
        var entry = ResolveEntryPoint(pkg, path, warnings);

        var managed = pkg.Dependencies
            .Select(kv => new ManagedDependency { Name = kv.Key, Version = kv.Value, Source = "package" })
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var runtime = new RuntimeInfo
        {
            Name = "Node.js",
            Version = pkg.NodeEngine,
            Tfm = pkg.NodeEngine,
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.Unknown,
            PublishMode = PublishMode.FrameworkDependent,
            FrameworkReferences = []
        };

        var network = new NetworkInfo
        {
            Ports = ports,
            ListensHttp = listensHttp,
            RequiresIngress = requiresIngress
        };

        var deps = new DependencyInfo
        {
            Managed = managed,
            Native = [],
            RequiresIcu = true,
            RequiresTimezone = false,
            RequiresGdiPlus = false
        };

        var envHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NODE_ENV"] = "production",
            ["NPM_CONFIG_LOGLEVEL"] = "warn"
        };
        if (listensHttp)
        {
            envHints["PORT"] = defaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Task.FromResult(new AppDescriptor
        {
            SourcePath = path,
            Kind = AppKind.NodeJs,
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

    private static (int Port, bool ListensHttp, bool RequiresIngress) ResolveNetwork(AppFlavor flavor)
    {
        return flavor switch
        {
            AppFlavor.NodeNext => (3000, true, true),
            AppFlavor.NodeNest => (3000, true, true),
            AppFlavor.NodeFastify => (3000, true, true),
            AppFlavor.NodeExpress => (3000, true, true),
            _ => (8080, false, false)
        };
    }

    private static EntryPoint ResolveEntryPoint(PackageJsonModel pkg, string path, List<string> warnings)
    {
        if (pkg.Scripts.TryGetValue("start", out var start) && !string.IsNullOrWhiteSpace(start))
        {
            return new EntryPoint
            {
                Path = path,
                AssemblyName = pkg.Name,
                StartupCommand = "npm",
                Arguments = ["start", "--silent"]
            };
        }

        if (!string.IsNullOrEmpty(pkg.Main))
        {
            return new EntryPoint
            {
                Path = Path.Combine(path, pkg.Main),
                AssemblyName = pkg.Name,
                StartupCommand = "node",
                Arguments = [pkg.Main]
            };
        }

        foreach (var candidate in new[] { "index.js", "server.js", "app.js", "main.js", "dist/index.js", "dist/main.js", "dist/server.js" })
        {
            if (File.Exists(Path.Combine(path, candidate)))
            {
                return new EntryPoint
                {
                    Path = Path.Combine(path, candidate),
                    AssemblyName = pkg.Name,
                    StartupCommand = "node",
                    Arguments = [candidate]
                };
            }
        }

        warnings.Add("could not resolve a node entry point; defaulting to `node .`");
        return new EntryPoint
        {
            Path = path,
            AssemblyName = pkg.Name,
            StartupCommand = "node",
            Arguments = ["."]
        };
    }
}
