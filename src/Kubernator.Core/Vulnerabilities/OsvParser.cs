using System.Text.Json;

namespace Kubernator.Core.Vulnerabilities;

internal static class OsvParser
{
    public static VulnerabilityRecord? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var affected = new List<AffectedPackage>();
        if (root.TryGetProperty("affected", out var affectedArray) && affectedArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in affectedArray.EnumerateArray())
            {
                var pkg = ParseAffected(entry);
                if (pkg is not null)
                {
                    affected.Add(pkg);
                }
            }
        }
        if (affected.Count == 0)
        {
            return null;
        }

        var references = new List<string>();
        if (root.TryGetProperty("references", out var refArray) && refArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in refArray.EnumerateArray())
            {
                if (r.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                {
                    references.Add(url.GetString() ?? string.Empty);
                }
            }
        }

        var severity = ExtractSeverity(root);

        return new VulnerabilityRecord
        {
            Id = idElement.GetString() ?? string.Empty,
            Summary = ReadString(root, "summary"),
            Details = ReadString(root, "details"),
            Severity = severity,
            Affected = affected,
            References = references,
            Published = ReadDate(root, "published"),
            Modified = ReadDate(root, "modified")
        };
    }

    private static AffectedPackage? ParseAffected(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!element.TryGetProperty("package", out var pkg) || pkg.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var ecosystem = ReadString(pkg, "ecosystem");
        var name = ReadString(pkg, "name");
        if (string.IsNullOrEmpty(ecosystem) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        var ranges = new List<VersionRange>();
        if (element.TryGetProperty("ranges", out var rangesArray) && rangesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var range in rangesArray.EnumerateArray())
            {
                ranges.AddRange(ParseRange(range));
            }
        }

        var versions = new List<string>();
        if (element.TryGetProperty("versions", out var versionsArray) && versionsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in versionsArray.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String)
                {
                    versions.Add(v.GetString() ?? string.Empty);
                }
            }
        }

        return new AffectedPackage
        {
            Ecosystem = ecosystem,
            Name = name,
            Ranges = ranges,
            Versions = versions
        };
    }

    private static IEnumerable<VersionRange> ParseRange(JsonElement range)
    {
        if (range.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        var type = ReadString(range, "type") ?? "ECOSYSTEM";
        if (!range.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        string? introduced = null;
        foreach (var ev in events.EnumerateArray())
        {
            if (ev.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (ev.TryGetProperty("introduced", out var ie) && ie.ValueKind == JsonValueKind.String)
            {
                introduced = ie.GetString();
                continue;
            }
            if (ev.TryGetProperty("fixed", out var fe) && fe.ValueKind == JsonValueKind.String)
            {
                yield return new VersionRange
                {
                    Type = type,
                    Introduced = introduced,
                    Fixed = fe.GetString()
                };
                introduced = null;
                continue;
            }
            if (ev.TryGetProperty("last_affected", out var le) && le.ValueKind == JsonValueKind.String)
            {
                yield return new VersionRange
                {
                    Type = type,
                    Introduced = introduced,
                    LastAffected = le.GetString()
                };
                introduced = null;
            }
        }

        if (introduced is not null)
        {
            yield return new VersionRange
            {
                Type = type,
                Introduced = introduced
            };
        }
    }

    private static string? ExtractSeverity(JsonElement root)
    {
        if (root.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in severity.EnumerateArray())
            {
                var type = ReadString(s, "type") ?? string.Empty;
                var score = ReadString(s, "score") ?? string.Empty;
                if (type.StartsWith("CVSS", StringComparison.Ordinal) && !string.IsNullOrEmpty(score))
                {
                    return $"{type}:{score}";
                }
            }
        }
        if (root.TryGetProperty("database_specific", out var dbSpecific) &&
            dbSpecific.ValueKind == JsonValueKind.Object &&
            dbSpecific.TryGetProperty("severity", out var ghsa) &&
            ghsa.ValueKind == JsonValueKind.String)
        {
            return ghsa.GetString();
        }
        return "UNKNOWN";
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement obj, string name)
    {
        var raw = ReadString(obj, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return dt;
        }
        return null;
    }
}
