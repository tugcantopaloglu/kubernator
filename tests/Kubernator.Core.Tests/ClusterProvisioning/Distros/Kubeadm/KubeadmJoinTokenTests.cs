using Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

namespace Kubernator.Core.Tests.ClusterProvisioning.Distros.Kubeadm;

public sealed class KubeadmJoinTokenTests
{
    [Fact]
    public void Round_trips_bootstrap_token_ca_hash_and_certificate_key()
    {
        var encoded = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", "cafef00d");

        var (bootstrapToken, caCertHash, certificateKey) = KubeadmJoinToken.Decode(encoded);

        bootstrapToken.Should().Be("abcdef.0123456789abcdef");
        caCertHash.Should().Be("deadbeef");
        certificateKey.Should().Be("cafef00d");
    }

    [Fact]
    public void Round_trips_with_no_certificate_key_for_a_worker_join()
    {
        var encoded = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", null);

        var (bootstrapToken, caCertHash, certificateKey) = KubeadmJoinToken.Decode(encoded);

        bootstrapToken.Should().Be("abcdef.0123456789abcdef");
        caCertHash.Should().Be("deadbeef");
        certificateKey.Should().BeNull();
    }

    [Fact]
    public void Decode_throws_on_malformed_token()
    {
        var act = () => KubeadmJoinToken.Decode("not-a-composite-token");

        act.Should().Throw<InvalidOperationException>();
    }
}
