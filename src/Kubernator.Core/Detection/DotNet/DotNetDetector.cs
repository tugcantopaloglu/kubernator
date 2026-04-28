using System.Text.Json;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Detection.DotNet;

public sealed class DotNetDetector : IAppDetector
{
    private readonly ILogger<DotNetDetector> logger;

    public DotNetDetector(ILogger<DotNetDetector> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.DotNet;

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

        var layouts = DotNetLayoutScanner.Scan(resolved);
        if (layouts.Count > 0)
        {
            var primary = SelectPrimary(layouts);
            return Task.FromResult(BuildPublishedResult(primary, layouts.Count, resolved, ct));
        }

        var sourceResult = DetectSource(resolved);
        if (sourceResult.Confidence > 0)
        {
            return Task.FromResult(sourceResult);
        }

        return Task.FromResult(DetectionResult.None(resolved));
    }

    private static DotNetPublishLayout SelectPrimary(IReadOnlyList<DotNetPublishLayout> layouts)
    {
        var withAll = layouts.FirstOrDefault(l => l.HasRuntimeConfig && l.HasMainAssembly);
        if (withAll is not null)
        {
            return withAll;
        }

        var withRuntime = layouts.FirstOrDefault(l => l.HasRuntimeConfig);
        return withRuntime ?? layouts[0];
    }

    private DetectionResult BuildPublishedResult(
        DotNetPublishLayout layout,
        int totalLayouts,
        string resolved,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var signals = new List<string>
        {
            $".deps.json: {Path.GetFileName(layout.DepsJsonPath)}"
        };

        var warnings = new List<string>();
        var flavor = AppFlavor.DotNetConsole;
        var confidence = 0.7;

        if (layout.HasRuntimeConfig)
        {
            signals.Add($".runtimeconfig.json: {Path.GetFileName(layout.RuntimeConfigPath!)}");
            confidence = 0.9;

            try
            {
                flavor = ReadFlavorFromRuntimeConfig(layout.RuntimeConfigPath!, signals, warnings);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse runtime config {Path}", layout.RuntimeConfigPath);
                warnings.Add($"runtimeconfig parse failed: {ex.Message}");
            }
        }
        else
        {
            warnings.Add("missing .runtimeconfig.json — output may not be a complete publish");
        }

        if (layout.HasMainAssembly)
        {
            signals.Add($"main assembly: {Path.GetFileName(layout.MainAssemblyPath!)}");
            confidence = Math.Min(1.0, confidence + 0.1);
        }

        if (layout.AppHostPath is not null)
        {
            signals.Add($"apphost: {Path.GetFileName(layout.AppHostPath)}");
        }

        if (totalLayouts > 1)
        {
            warnings.Add($"{totalLayouts} candidate publish outputs found; using {layout.AssemblyBaseName}");
        }

        return new DetectionResult
        {
            SourcePath = resolved,
            Kind = AppKind.DotNet,
            Flavor = flavor,
            Confidence = confidence,
            Signals = signals,
            Warnings = warnings
        };
    }

    private static AppFlavor ReadFlavorFromRuntimeConfig(string path, List<string> signals, List<string> warnings)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("runtimeOptions", out var options))
        {
            return AppFlavor.DotNetConsole;
        }

        var frameworks = new List<string>();
        if (options.TryGetProperty("framework", out var single))
        {
            if (single.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                frameworks.Add(name.GetString() ?? string.Empty);
            }
        }
        if (options.TryGetProperty("frameworks", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in list.EnumerateArray())
            {
                if (f.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    frameworks.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        foreach (var fw in frameworks)
        {
            signals.Add($"framework: {fw}");
        }

        if (frameworks.Any(f => f.Equals("Microsoft.AspNetCore.App", StringComparison.Ordinal)))
        {
            return AppFlavor.DotNetAspNetCore;
        }

        if (frameworks.Any(f => f.Equals("Microsoft.WindowsDesktop.App", StringComparison.Ordinal)))
        {
            warnings.Add("Microsoft.WindowsDesktop.App is desktop-only; not suitable for Linux containers");
            return AppFlavor.DotNetConsole;
        }

        return AppFlavor.DotNetConsole;
    }

    private static DetectionResult DetectSource(string root)
    {
        var csproj = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (csproj is not null)
        {
            return new DetectionResult
            {
                SourcePath = root,
                Kind = AppKind.DotNet,
                Flavor = AppFlavor.Unknown,
                Confidence = 0.55,
                Signals = [$".csproj: {Path.GetRelativePath(root, csproj)}"],
                Warnings = ["source tree detected; publish first for accurate analysis"]
            };
        }

        var sln = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (sln is not null)
        {
            return new DetectionResult
            {
                SourcePath = root,
                Kind = AppKind.DotNet,
                Flavor = AppFlavor.Unknown,
                Confidence = 0.45,
                Signals = [$".sln: {Path.GetFileName(sln)}"],
                Warnings = ["solution detected; publish a project before containerizing"]
            };
        }

        return DetectionResult.None(root);
    }
}
