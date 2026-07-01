namespace Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

internal static class KubeadmJoinToken
{
    public static string Encode(string bootstrapToken, string caCertHash, string? certificateKey) =>
        $"{bootstrapToken}|{caCertHash}|{certificateKey}";

    public static (string BootstrapToken, string CaCertHash, string? CertificateKey) Decode(string token)
    {
        var parts = token.Split('|', 3);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("malformed kubeadm join token");
        }
        return (parts[0], parts[1], parts[2].Length == 0 ? null : parts[2]);
    }
}
