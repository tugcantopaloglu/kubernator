using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Detection.Go;

public sealed class GoDetector : IAppDetector
{
    public AppKind Handles => AppKind.Go;

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

        var goMod = Path.Combine(resolved, "go.mod");
        if (File.Exists(goMod))
        {
            return Task.FromResult(new DetectionResult
            {
                SourcePath = resolved,
                Kind = AppKind.Go,
                Flavor = AppFlavor.GoBinary,
                Confidence = 0.85,
                Signals = ["go.mod"],
                Warnings = ["source tree detected; build a static binary before bundling"]
            });
        }

        var binary = LocateLikelyBinary(resolved);
        if (binary is not null)
        {
            return Task.FromResult(new DetectionResult
            {
                SourcePath = resolved,
                Kind = AppKind.Go,
                Flavor = AppFlavor.GoBinary,
                Confidence = 0.7,
                Signals = [$"binary: {Path.GetFileName(binary)}"],
                Warnings = ["binary detected via heuristic; ensure it is statically linked"]
            });
        }

        return Task.FromResult(DetectionResult.None(resolved));
    }

    private static string? LocateLikelyBinary(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (IsElfBinary(file) || IsMachOBinary(file) || IsPeBinary(file))
            {
                return file;
            }
        }
        return null;
    }

    private static bool IsElfBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 && magic[0] == 0x7F && magic[1] == 0x45 && magic[2] == 0x4C && magic[3] == 0x46;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMachOBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) != 4)
            {
                return false;
            }
            return (magic[0] == 0xCF && magic[1] == 0xFA && magic[2] == 0xED && magic[3] == 0xFE)
                || (magic[0] == 0xFE && magic[1] == 0xED && magic[2] == 0xFA && magic[3] == 0xCF);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPeBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[2];
            return stream.Read(magic) == 2 && magic[0] == 0x4D && magic[1] == 0x5A;
        }
        catch
        {
            return false;
        }
    }
}
