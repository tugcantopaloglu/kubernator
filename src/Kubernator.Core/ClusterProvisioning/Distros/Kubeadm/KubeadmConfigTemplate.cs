using System.Text;

namespace Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

internal static class KubeadmConfigTemplate
{
    public static string RenderInit(ServerBootstrapOptions options)
    {
        var controlPlaneEndpoint = options.TlsSans.Count > 0 ? options.TlsSans[0] : options.AdvertiseAddress;

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: kubeadm.k8s.io/v1beta4");
        sb.AppendLine("kind: InitConfiguration");
        sb.AppendLine("localAPIEndpoint:");
        sb.AppendLine($"  advertiseAddress: {options.AdvertiseAddress}");
        sb.AppendLine("  bindPort: 6443");
        sb.AppendLine("---");
        sb.AppendLine("apiVersion: kubeadm.k8s.io/v1beta4");
        sb.AppendLine("kind: ClusterConfiguration");
        sb.AppendLine($"kubernetesVersion: {options.Version}");
        sb.AppendLine($"clusterName: {options.ClusterName}");
        sb.AppendLine($"controlPlaneEndpoint: {controlPlaneEndpoint}:6443");
        if (options.TlsSans.Count > 0)
        {
            sb.AppendLine("apiServer:");
            sb.AppendLine("  certSANs:");
            foreach (var san in options.TlsSans)
            {
                sb.AppendLine($"    - {san}");
            }
        }
        sb.AppendLine("networking:");
        sb.AppendLine($"  podSubnet: {options.PodCidr}");
        return sb.ToString();
    }

    public static string RenderJoinControlPlane(ServerBootstrapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.JoinServerUrl))
        {
            throw new ArgumentException("JoinServerUrl is required for a non-initial server", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("Token is required for a non-initial server", nameof(options));
        }

        var (bootstrapToken, caCertHash, certificateKey) = KubeadmJoinToken.Decode(options.Token);
        if (string.IsNullOrWhiteSpace(certificateKey))
        {
            throw new ArgumentException("Token is missing a certificate key, required to join an additional control-plane node", nameof(options));
        }

        var sb = new StringBuilder();
        AppendDiscovery(sb, bootstrapToken, caCertHash, StripScheme(options.JoinServerUrl));
        sb.AppendLine("controlPlane:");
        sb.AppendLine("  localAPIEndpoint:");
        sb.AppendLine($"    advertiseAddress: {options.AdvertiseAddress}");
        sb.AppendLine("    bindPort: 6443");
        sb.AppendLine($"  certificateKey: \"{certificateKey}\"");
        return sb.ToString();
    }

    public static string RenderJoinWorker(AgentJoinOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.JoinServerUrl))
        {
            throw new ArgumentException("JoinServerUrl is required", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("Token is required", nameof(options));
        }

        var (bootstrapToken, caCertHash, _) = KubeadmJoinToken.Decode(options.Token);

        var sb = new StringBuilder();
        AppendDiscovery(sb, bootstrapToken, caCertHash, StripScheme(options.JoinServerUrl));
        return sb.ToString();
    }

    private static void AppendDiscovery(StringBuilder sb, string bootstrapToken, string caCertHash, string apiServerEndpoint)
    {
        sb.AppendLine("apiVersion: kubeadm.k8s.io/v1beta4");
        sb.AppendLine("kind: JoinConfiguration");
        sb.AppendLine("discovery:");
        sb.AppendLine("  bootstrapToken:");
        sb.AppendLine($"    token: \"{bootstrapToken}\"");
        sb.AppendLine($"    apiServerEndpoint: {apiServerEndpoint}");
        sb.AppendLine($"    caCertHashes: [\"{caCertHash}\"]");
    }

    private static string StripScheme(string url)
    {
        var idx = url.IndexOf("://", StringComparison.Ordinal);
        return idx >= 0 ? url[(idx + 3)..] : url;
    }
}
