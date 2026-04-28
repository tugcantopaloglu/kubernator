using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Detection.Static;

public sealed class StaticWebDetector : IAppDetector
{
    public AppKind Handles => AppKind.StaticWeb;

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

        var indexCandidates = new[] { "index.html", "index.htm" };
        string? rootIndex = null;
        string? webRoot = resolved;

        foreach (var candidate in indexCandidates)
        {
            if (File.Exists(Path.Combine(resolved, candidate)))
            {
                rootIndex = candidate;
                break;
            }
        }

        if (rootIndex is null)
        {
            foreach (var subdir in new[] { "dist", "build", "public", "out", "wwwroot" })
            {
                var subPath = Path.Combine(resolved, subdir);
                if (!Directory.Exists(subPath))
                {
                    continue;
                }
                foreach (var candidate in indexCandidates)
                {
                    if (File.Exists(Path.Combine(subPath, candidate)))
                    {
                        rootIndex = candidate;
                        webRoot = subPath;
                        break;
                    }
                }
                if (rootIndex is not null)
                {
                    break;
                }
            }
        }

        if (rootIndex is null)
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var hasPackageJson = File.Exists(Path.Combine(resolved, "package.json"));
        var hasNodeModules = Directory.Exists(Path.Combine(resolved, "node_modules"));
        var hasServerSideAssets =
            File.Exists(Path.Combine(resolved, "package.json")) &&
            (Directory.Exists(Path.Combine(resolved, "pages")) || Directory.Exists(Path.Combine(resolved, "app")));

        if ((hasPackageJson && hasNodeModules) || hasServerSideAssets)
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var hasAssets = Directory.Exists(Path.Combine(webRoot!, "assets"))
                        || Directory.Exists(Path.Combine(webRoot, "static"))
                        || Directory.Exists(Path.Combine(webRoot, "js"))
                        || Directory.Exists(Path.Combine(webRoot, "css"));

        var flavor = hasAssets ? AppFlavor.StaticSpa : AppFlavor.StaticHtml;
        var signals = new List<string> { $"web root: {Path.GetRelativePath(resolved, webRoot)}/{rootIndex}" };
        if (hasAssets)
        {
            signals.Add("bundled assets present");
        }

        return Task.FromResult(new DetectionResult
        {
            SourcePath = webRoot,
            Kind = AppKind.StaticWeb,
            Flavor = flavor,
            Confidence = 0.85,
            Signals = signals,
            Warnings = []
        });
    }
}
