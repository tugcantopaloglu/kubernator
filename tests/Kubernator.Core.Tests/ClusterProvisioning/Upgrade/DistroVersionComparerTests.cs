using Kubernator.Core.ClusterProvisioning.Upgrade;

namespace Kubernator.Core.Tests.ClusterProvisioning.Upgrade;

public sealed class DistroVersionComparerTests
{
    [Fact]
    public void Same_core_and_build_does_not_need_upgrade()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.4+rke2r1", "v1.30.4+rke2r1").Should().BeFalse();
    }

    [Fact]
    public void Older_patch_needs_upgrade()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.3+rke2r1", "v1.30.4+rke2r1").Should().BeTrue();
    }

    [Fact]
    public void Older_minor_needs_upgrade()
    {
        DistroVersionComparer.NeedsUpgrade("v1.29.9+rke2r1", "v1.30.0+rke2r1").Should().BeTrue();
    }

    [Fact]
    public void Same_core_different_build_suffix_needs_upgrade()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.4+rke2r1", "v1.30.4+rke2r2").Should().BeTrue();
    }

    [Fact]
    public void Same_core_different_k3s_build_suffix_needs_upgrade()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.4+k3s1", "v1.30.4+k3s2").Should().BeTrue();
    }

    [Fact]
    public void Vanilla_kubeadm_tag_with_no_plus_suffix_compares_correctly()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.4", "v1.30.4").Should().BeFalse();
        DistroVersionComparer.NeedsUpgrade("v1.30.4", "v1.30.5").Should().BeTrue();
    }

    [Fact]
    public void Unparseable_placeholder_strings_fall_back_to_ordinal_inequality()
    {
        DistroVersionComparer.NeedsUpgrade("v1", "v2").Should().BeTrue();
        DistroVersionComparer.NeedsUpgrade("v1", "v1").Should().BeFalse();
    }

    [Fact]
    public void Mixed_parseable_and_unparseable_falls_back_to_string_comparison()
    {
        DistroVersionComparer.NeedsUpgrade("v1.30.4+rke2r1", "v1").Should().BeTrue();
    }
}
