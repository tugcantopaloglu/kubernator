using System.Text.Json;

namespace Kubernator.Core.Analysis.DotNet;

internal static class DepsJsonReader
{
    public static DepsJsonModel Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;
        var runtimeTarget = root.TryGetProperty("runtimeTarget", out var rt) && rt.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;

        var rid = ExtractRid(runtimeTarget);

        var libraries = new List<DepsLibrary>();
        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in libs.EnumerateObject())
            {
                var slash = prop.Name.IndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }
                var name = prop.Name[..slash];
                var version = prop.Name[(slash + 1)..];
                var type = prop.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() ?? "unknown"
                    : "unknown";

                libraries.Add(new DepsLibrary
                {
                    Id = prop.Name,
                    Name = name,
                    Version = version,
                    Type = type
                });
            }
        }

        var natives = new List<string>();
        if (root.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Object)
        {
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                foreach (var lib in target.Value.EnumerateObject())
                {
                    if (lib.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (lib.Value.TryGetProperty("runtimeTargets", out var rtTargets) &&
                        rtTargets.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var asset in rtTargets.EnumerateObject())
                        {
                            if (asset.Value.TryGetProperty("assetType", out var at) &&
                                at.ValueKind == JsonValueKind.String &&
                                string.Equals(at.GetString(), "native", StringComparison.Ordinal))
                            {
                                natives.Add(Path.GetFileName(asset.Name));
                            }
                        }
                    }
                    if (lib.Value.TryGetProperty("native", out var native) &&
                        native.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var asset in native.EnumerateObject())
                        {
                            natives.Add(Path.GetFileName(asset.Name));
                        }
                    }
                }
            }
        }

        return new DepsJsonModel
        {
            RuntimeTargetName = runtimeTarget,
            RuntimeIdentifier = rid,
            Libraries = libraries,
            NativeFiles = natives.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static string? ExtractRid(string? runtimeTargetName)
    {
        if (string.IsNullOrEmpty(runtimeTargetName))
        {
            return null;
        }
        var slash = runtimeTargetName.IndexOf('/');
        if (slash < 0 || slash == runtimeTargetName.Length - 1)
        {
            return null;
        }
        return runtimeTargetName[(slash + 1)..];
    }
}
