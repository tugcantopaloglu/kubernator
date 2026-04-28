using System.Text.Json;

namespace Kubernator.Core.Detection.Node;

internal static class PackageJsonReader
{
    public static PackageJsonModel Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new PackageJsonModel();
        }

        return new PackageJsonModel
        {
            Name = ReadString(root, "name"),
            Version = ReadString(root, "version"),
            Main = ReadString(root, "main"),
            Type = ReadString(root, "type"),
            Scripts = ReadStringMap(root, "scripts"),
            Dependencies = ReadStringMap(root, "dependencies"),
            DevDependencies = ReadStringMap(root, "devDependencies"),
            NodeEngine = ReadStringMap(root, "engines").TryGetValue("node", out var node) ? node : null,
            Bin = ReadBin(root)
        };
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in prop.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                map[p.Name] = p.Value.GetString() ?? string.Empty;
            }
        }
        return map;
    }

    private static IReadOnlyDictionary<string, string> ReadBin(JsonElement obj)
    {
        if (!obj.TryGetProperty("bin", out var prop))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (prop.ValueKind == JsonValueKind.String)
        {
            var name = ReadString(obj, "name") ?? "app";
            map[name] = prop.GetString() ?? string.Empty;
        }
        else if (prop.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in prop.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    map[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }
        }
        return map;
    }
}
