using System.Text.Json;

namespace Kubernator.Core.Analysis.DotNet;

internal static class RuntimeConfigReader
{
    public static RuntimeConfig Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("runtimeOptions", out var options))
        {
            return new RuntimeConfig();
        }

        var tfm = options.TryGetProperty("tfm", out var tfmElement) && tfmElement.ValueKind == JsonValueKind.String
            ? tfmElement.GetString()
            : null;

        var rollForward = options.TryGetProperty("rollForward", out var rfElement) && rfElement.ValueKind == JsonValueKind.String
            ? rfElement.GetString()
            : null;

        var frameworks = new List<RuntimeFrameworkRef>();
        if (options.TryGetProperty("framework", out var single))
        {
            var fw = ReadFramework(single);
            if (fw is not null)
            {
                frameworks.Add(fw);
            }
        }
        if (options.TryGetProperty("frameworks", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in list.EnumerateArray())
            {
                var fw = ReadFramework(f);
                if (fw is not null)
                {
                    frameworks.Add(fw);
                }
            }
        }

        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        if (options.TryGetProperty("configProperties", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cfg.EnumerateObject())
            {
                props[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    _ => prop.Value.GetRawText()
                };
            }
        }

        return new RuntimeConfig
        {
            Tfm = tfm,
            RollForward = rollForward,
            Frameworks = frameworks,
            ConfigProperties = props
        };
    }

    private static RuntimeFrameworkRef? ReadFramework(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var version = element.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String
            ? ver.GetString()
            : null;
        return new RuntimeFrameworkRef
        {
            Name = name.GetString() ?? string.Empty,
            Version = version
        };
    }
}
