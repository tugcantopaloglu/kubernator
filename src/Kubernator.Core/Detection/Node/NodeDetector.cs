using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Detection.Node;

public sealed class NodeDetector : IAppDetector
{
    private readonly ILogger<NodeDetector> logger;

    public NodeDetector(ILogger<NodeDetector> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.NodeJs;

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

        var packageJson = Path.Combine(resolved, "package.json");
        if (!File.Exists(packageJson))
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var signals = new List<string> { "package.json" };
        var warnings = new List<string>();
        var confidence = 0.7;
        var flavor = AppFlavor.NodeGeneric;

        PackageJsonModel? pkg = null;
        try
        {
            pkg = PackageJsonReader.Read(packageJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse package.json");
            warnings.Add($"package.json parse failed: {ex.Message}");
        }

        if (pkg is not null)
        {
            confidence = 0.85;
            if (pkg.Dependencies.ContainsKey("next") || pkg.DevDependencies.ContainsKey("next"))
            {
                flavor = AppFlavor.NodeNext;
                signals.Add("framework: next");
            }
            else if (pkg.Dependencies.ContainsKey("@nestjs/core"))
            {
                flavor = AppFlavor.NodeNest;
                signals.Add("framework: @nestjs/core");
            }
            else if (pkg.Dependencies.ContainsKey("fastify"))
            {
                flavor = AppFlavor.NodeFastify;
                signals.Add("framework: fastify");
            }
            else if (pkg.Dependencies.ContainsKey("express"))
            {
                flavor = AppFlavor.NodeExpress;
                signals.Add("framework: express");
            }

            if (Directory.Exists(Path.Combine(resolved, "node_modules")))
            {
                signals.Add("node_modules present");
                confidence = Math.Min(1.0, confidence + 0.1);
            }
            else
            {
                warnings.Add("node_modules missing; install before bundling for offline use");
            }

            if (!string.IsNullOrEmpty(pkg.Main) && File.Exists(Path.Combine(resolved, pkg.Main)))
            {
                signals.Add($"main: {pkg.Main}");
            }
            if (pkg.Scripts.TryGetValue("start", out var start))
            {
                signals.Add($"start: {start}");
            }
            if (!string.IsNullOrEmpty(pkg.NodeEngine))
            {
                signals.Add($"engines.node: {pkg.NodeEngine}");
            }
        }

        return Task.FromResult(new DetectionResult
        {
            SourcePath = resolved,
            Kind = AppKind.NodeJs,
            Flavor = flavor,
            Confidence = confidence,
            Signals = signals,
            Warnings = warnings
        });
    }
}
