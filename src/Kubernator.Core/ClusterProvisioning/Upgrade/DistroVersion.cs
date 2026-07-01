using System.Globalization;

namespace Kubernator.Core.ClusterProvisioning.Upgrade;

public readonly record struct DistroVersion
{
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required int Patch { get; init; }
    public string? Prerelease { get; init; }
    public string? Build { get; init; }
    public required string Raw { get; init; }

    public static bool TryParse(string? value, out DistroVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value;
        var core = raw.AsSpan().TrimStart("vV").ToString();

        string? build = null;
        var plusIdx = core.IndexOf('+');
        if (plusIdx >= 0)
        {
            build = core[(plusIdx + 1)..];
            core = core[..plusIdx];
        }

        string? prerelease = null;
        var dashIdx = core.IndexOf('-');
        if (dashIdx >= 0)
        {
            prerelease = core[(dashIdx + 1)..];
            core = core[..dashIdx];
        }

        var parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new DistroVersion
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            Prerelease = prerelease,
            Build = build,
            Raw = raw
        };
        return true;
    }

    public int CompareCoreTo(DistroVersion other)
    {
        var byMajor = Major.CompareTo(other.Major);
        if (byMajor != 0)
        {
            return byMajor;
        }
        var byMinor = Minor.CompareTo(other.Minor);
        if (byMinor != 0)
        {
            return byMinor;
        }
        var byPatch = Patch.CompareTo(other.Patch);
        if (byPatch != 0)
        {
            return byPatch;
        }
        return string.CompareOrdinal(Prerelease ?? string.Empty, other.Prerelease ?? string.Empty);
    }
}

public static class DistroVersionComparer
{
    public static bool NeedsUpgrade(string? installedVersion, string targetVersion)
    {
        if (!DistroVersion.TryParse(installedVersion, out var installed) || !DistroVersion.TryParse(targetVersion, out var target))
        {
            return !string.Equals(installedVersion, targetVersion, StringComparison.Ordinal);
        }

        if (installed.CompareCoreTo(target) != 0)
        {
            return true;
        }
        return !string.Equals(installed.Build ?? string.Empty, target.Build ?? string.Empty, StringComparison.Ordinal);
    }
}
