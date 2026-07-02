using Kubernator.Core.ClusterProvisioning.Distros;

namespace Kubernator.Web.Api;

internal static class ClusterDistroParsing
{
    public static bool TryParse(string raw, out DistroKind distro)
    {
        switch (raw.ToLowerInvariant())
        {
            case "rke2":
                distro = DistroKind.Rke2;
                return true;
            case "k3s":
                distro = DistroKind.K3s;
                return true;
            case "kubeadm":
            case "kubeadm-native":
                distro = DistroKind.KubeadmNative;
                return true;
            default:
                distro = default;
                return false;
        }
    }

    public static string ToWireString(DistroKind distro) => distro switch
    {
        DistroKind.Rke2 => "rke2",
        DistroKind.K3s => "k3s",
        DistroKind.KubeadmNative => "kubeadm-native",
        _ => throw new ArgumentOutOfRangeException(nameof(distro), distro, null)
    };
}
