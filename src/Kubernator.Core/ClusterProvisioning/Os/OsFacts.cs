namespace Kubernator.Core.ClusterProvisioning.Os;

public enum OsFamily
{
    Unknown,
    DebianLike,
    RhelLike
}

public enum FirewallKind
{
    Unknown,
    None,
    Ufw,
    Firewalld
}

public sealed record OsFacts
{
    public required OsFamily Family { get; init; }
    public required string DistroId { get; init; }
    public required string VersionId { get; init; }
    public required string Arch { get; init; }
    public required bool SelinuxEnforcing { get; init; }
    public required FirewallKind Firewall { get; init; }
    public required bool SwapEnabled { get; init; }
}
