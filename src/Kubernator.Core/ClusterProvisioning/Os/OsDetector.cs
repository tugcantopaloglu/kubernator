using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Os;

public sealed class OsDetector : IOsDetector
{
    private const string Script = """
        echo OS_RELEASE_START
        cat /etc/os-release 2>/dev/null
        echo OS_RELEASE_END
        echo ARCH=$(uname -m)
        echo SELINUX=$(getenforce 2>/dev/null || echo Disabled)
        if systemctl is-active firewalld >/dev/null 2>&1; then
            echo FIREWALL=firewalld
        elif command -v ufw >/dev/null 2>&1; then
            echo FIREWALL=ufw
        else
            echo FIREWALL=none
        fi
        echo SWAP=$(awk 'NR>1' /proc/swaps 2>/dev/null | wc -l)
        """;

    private static readonly Dictionary<string, OsFamily> FamilyById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ubuntu"] = OsFamily.DebianLike,
        ["debian"] = OsFamily.DebianLike,
        ["rhel"] = OsFamily.RhelLike,
        ["centos"] = OsFamily.RhelLike,
        ["rocky"] = OsFamily.RhelLike,
        ["almalinux"] = OsFamily.RhelLike,
        ["ol"] = OsFamily.RhelLike,
        ["fedora"] = OsFamily.RhelLike
    };

    public async Task<OsFacts> DetectAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var outcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = Script, Timeout = TimeSpan.FromSeconds(30) },
            null,
            ct);

        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"OS detection failed: {outcome.StandardError}");
        }

        var lines = outcome.StandardOutput.Split('\n');
        var osRelease = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inOsRelease = false;
        string arch = "unknown", selinux = "Disabled", firewall = "none", swap = "0";

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line == "OS_RELEASE_START") { inOsRelease = true; continue; }
            if (line == "OS_RELEASE_END") { inOsRelease = false; continue; }
            if (inOsRelease)
            {
                var idx = line.IndexOf('=');
                if (idx > 0)
                {
                    osRelease[line[..idx]] = line[(idx + 1)..].Trim('"');
                }
                continue;
            }
            if (line.StartsWith("ARCH=", StringComparison.Ordinal)) { arch = line["ARCH=".Length..]; continue; }
            if (line.StartsWith("SELINUX=", StringComparison.Ordinal)) { selinux = line["SELINUX=".Length..]; continue; }
            if (line.StartsWith("FIREWALL=", StringComparison.Ordinal)) { firewall = line["FIREWALL=".Length..]; continue; }
            if (line.StartsWith("SWAP=", StringComparison.Ordinal)) { swap = line["SWAP=".Length..]; continue; }
        }

        var distroId = osRelease.GetValueOrDefault("ID", "unknown").ToLowerInvariant();
        var versionId = osRelease.GetValueOrDefault("VERSION_ID", "unknown");
        var family = FamilyById.GetValueOrDefault(distroId, OsFamily.Unknown);

        return new OsFacts
        {
            Family = family,
            DistroId = distroId,
            VersionId = versionId,
            Arch = NormalizeArch(arch),
            SelinuxEnforcing = string.Equals(selinux.Trim(), "Enforcing", StringComparison.OrdinalIgnoreCase),
            Firewall = firewall switch
            {
                "firewalld" => FirewallKind.Firewalld,
                "ufw" => FirewallKind.Ufw,
                "none" => FirewallKind.None,
                _ => FirewallKind.Unknown
            },
            SwapEnabled = int.TryParse(swap.Trim(), out var swapLines) && swapLines > 0
        };
    }

    private static string NormalizeArch(string uname) => uname.Trim() switch
    {
        "x86_64" => "amd64",
        "aarch64" => "arm64",
        "arm64" => "arm64",
        "amd64" => "amd64",
        _ => uname.Trim()
    };
}
