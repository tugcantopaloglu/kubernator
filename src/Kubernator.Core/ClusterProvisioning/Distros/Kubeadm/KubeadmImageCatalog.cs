namespace Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

public static class KubeadmImageCatalog
{
    private sealed record MinorImages(string Pause, string Etcd, string CoreDns);

    private static readonly Dictionary<string, MinorImages> ByMinor = new(StringComparer.Ordinal)
    {
        ["1.28"] = new MinorImages("3.9", "3.5.10-0", "v1.10.1"),
        ["1.29"] = new MinorImages("3.9", "3.5.10-0", "v1.11.1"),
        ["1.30"] = new MinorImages("3.9", "3.5.12-0", "v1.11.1"),
        ["1.31"] = new MinorImages("3.10", "3.5.15-0", "v1.11.3"),
        ["1.32"] = new MinorImages("3.10", "3.5.16-0", "v1.11.3")
    };

    public static IReadOnlyList<string> ImagesFor(string kubeVersion)
    {
        var minor = ExtractMinor(kubeVersion);
        if (!ByMinor.TryGetValue(minor, out var images))
        {
            throw new NotSupportedException(
                $"no kubeadm image catalog entry for Kubernetes {minor} (from version '{kubeVersion}') — add one to {nameof(KubeadmImageCatalog)}");
        }

        return
        [
            $"registry.k8s.io/kube-apiserver:{kubeVersion}",
            $"registry.k8s.io/kube-controller-manager:{kubeVersion}",
            $"registry.k8s.io/kube-scheduler:{kubeVersion}",
            $"registry.k8s.io/kube-proxy:{kubeVersion}",
            $"registry.k8s.io/pause:{images.Pause}",
            $"registry.k8s.io/etcd:{images.Etcd}",
            $"registry.k8s.io/coredns/coredns:{images.CoreDns}"
        ];
    }

    private static string ExtractMinor(string kubeVersion)
    {
        var trimmed = kubeVersion.TrimStart('v', 'V');
        var parts = trimmed.Split('.');
        if (parts.Length < 2)
        {
            throw new ArgumentException($"cannot extract major.minor from version '{kubeVersion}'", nameof(kubeVersion));
        }
        return $"{parts[0]}.{parts[1]}";
    }
}
