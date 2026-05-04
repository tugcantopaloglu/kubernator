using System.Globalization;

namespace Kubernator.Core.Vulnerabilities;

internal static class SemverComparator
{
    public static int Compare(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 0;
        }

        var (coreA, preA) = SplitCoreAndPrerelease(a);
        var (coreB, preB) = SplitCoreAndPrerelease(b);

        var coreCmp = CompareCore(coreA, coreB);
        if (coreCmp != 0)
        {
            return coreCmp;
        }

        if (string.IsNullOrEmpty(preA) && string.IsNullOrEmpty(preB))
        {
            return 0;
        }
        if (string.IsNullOrEmpty(preA))
        {
            return 1;
        }
        if (string.IsNullOrEmpty(preB))
        {
            return -1;
        }
        return ComparePrerelease(preA, preB);
    }

    public static bool IsAffected(string version, VersionRange range)
    {
        if (range.Type.Equals("GIT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        var introduced = string.IsNullOrEmpty(range.Introduced) || range.Introduced == "0"
            ? null
            : range.Introduced;
        var fixedAt = string.IsNullOrEmpty(range.Fixed) ? null : range.Fixed;
        var lastAffected = string.IsNullOrEmpty(range.LastAffected) ? null : range.LastAffected;

        if (introduced is null && fixedAt is null && lastAffected is null)
        {
            return true;
        }

        if (introduced is not null && Compare(version, introduced) < 0)
        {
            return false;
        }
        if (fixedAt is not null && Compare(version, fixedAt) >= 0)
        {
            return false;
        }
        if (lastAffected is not null && Compare(version, lastAffected) > 0)
        {
            return false;
        }
        return true;
    }

    public static bool IsAffected(string version, AffectedPackage package)
    {
        if (package.Versions.Any(v => string.Equals(v, version, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (package.Ranges.Count == 0)
        {
            return false;
        }
        return package.Ranges.Any(r => IsAffected(version, r));
    }

    private static (string Core, string Prerelease) SplitCoreAndPrerelease(string raw)
    {
        var trimmed = raw.TrimStart('v', 'V').Trim();
        var plus = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }
        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return (trimmed, string.Empty);
        }
        return (trimmed[..dash], trimmed[(dash + 1)..]);
    }

    private static int CompareCore(string a, string b)
    {
        var partsA = a.Split('.');
        var partsB = b.Split('.');
        var len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            var va = i < partsA.Length ? partsA[i] : "0";
            var vb = i < partsB.Length ? partsB[i] : "0";
            var cmp = CompareNumericOrString(va, vb);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return 0;
    }

    private static int ComparePrerelease(string a, string b)
    {
        var partsA = a.Split('.');
        var partsB = b.Split('.');
        var len = Math.Min(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            var cmp = CompareNumericOrString(partsA[i], partsB[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return partsA.Length.CompareTo(partsB.Length);
    }

    private static int CompareNumericOrString(string a, string b)
    {
        var aIsNum = int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ai);
        var bIsNum = int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bi);
        if (aIsNum && bIsNum)
        {
            return ai.CompareTo(bi);
        }
        if (aIsNum)
        {
            return -1;
        }
        if (bIsNum)
        {
            return 1;
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
