using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kubernator.Core.Analysis.DotNet;

internal sealed record AppSettingsHints
{
    public IReadOnlyList<int> Ports { get; init; } = [];
    public IReadOnlyList<string> Urls { get; init; } = [];
    public bool ListensHttp { get; init; }
    public bool ListensHttps { get; init; }
    public IReadOnlyDictionary<string, string> EnvironmentHints { get; init; } =
        new Dictionary<string, string>();
}

internal static partial class AppSettingsScanner
{
    [GeneratedRegex(@"(?<scheme>https?)://(?<host>[^:/\s]+)(?::(?<port>\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public static AppSettingsHints Scan(string root)
    {
        var ports = new HashSet<int>();
        var urls = new List<string>();
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        bool http = false;
        bool https = false;

        foreach (var file in EnumerateConfigFiles(root))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                CollectFromConfig(doc.RootElement, ports, urls, env, ref http, ref https);
            }
            catch
            {
            }
        }

        return new AppSettingsHints
        {
            Ports = [.. ports.OrderBy(p => p)],
            Urls = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ListensHttp = http,
            ListensHttps = https,
            EnvironmentHints = env
        };
    }

    private static IEnumerable<string> EnumerateConfigFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var f in Directory.EnumerateFiles(root, "appsettings*.json", SearchOption.TopDirectoryOnly))
        {
            yield return f;
        }
    }

    private static void CollectFromConfig(
        JsonElement root,
        HashSet<int> ports,
        List<string> urls,
        Dictionary<string, string> env,
        ref bool http,
        ref bool https)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetStringCi(root, "urls", out var urlsString))
        {
            ParseUrlList(urlsString!, ports, urls, ref http, ref https);
        }
        if (TryGetStringCi(root, "applicationUrl", out var appUrl))
        {
            ParseUrlList(appUrl!, ports, urls, ref http, ref https);
        }

        if (TryGetObjectCi(root, "Kestrel", out var kestrel) &&
            TryGetObjectCi(kestrel, "Endpoints", out var endpoints))
        {
            foreach (var ep in endpoints.EnumerateObject())
            {
                if (ep.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (TryGetStringCi(ep.Value, "Url", out var url) && url is not null)
                {
                    ParseUrlList(url, ports, urls, ref http, ref https);
                }
            }
        }
    }

    private static void ParseUrlList(string value, HashSet<int> ports, List<string> urls, ref bool http, ref bool https)
    {
        foreach (var raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            urls.Add(raw);
            var match = UrlRegex().Match(raw);
            if (!match.Success)
            {
                continue;
            }
            var scheme = match.Groups["scheme"].Value.ToLowerInvariant();
            if (scheme == "http")
            {
                http = true;
            }
            if (scheme == "https")
            {
                https = true;
            }

            if (match.Groups["port"].Success && int.TryParse(match.Groups["port"].Value, out var p))
            {
                ports.Add(p);
            }
            else if (scheme == "http")
            {
                ports.Add(80);
            }
            else if (scheme == "https")
            {
                ports.Add(443);
            }
        }
    }

    private static bool TryGetStringCi(JsonElement obj, string name, out string? value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                prop.Value.ValueKind == JsonValueKind.String)
            {
                value = prop.Value.GetString();
                return value is not null;
            }
        }
        value = null;
        return false;
    }

    private static bool TryGetObjectCi(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                prop.Value.ValueKind == JsonValueKind.Object)
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
