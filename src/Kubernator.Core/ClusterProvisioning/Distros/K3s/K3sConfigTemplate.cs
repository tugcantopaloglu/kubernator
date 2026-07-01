using System.Text;

namespace Kubernator.Core.ClusterProvisioning.Distros.K3s;

public static class K3sConfigTemplate
{
    public static string RenderServer(ServerBootstrapOptions options)
    {
        var sb = new StringBuilder();

        if (options.IsFirstServer)
        {
            sb.AppendLine("cluster-init: true");
            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                sb.AppendLine($"token: \"{Escape(options.Token)}\"");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.JoinServerUrl))
            {
                throw new ArgumentException("JoinServerUrl is required for a non-initial server", nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.Token))
            {
                throw new ArgumentException("Token is required for a non-initial server", nameof(options));
            }
            sb.AppendLine($"server: {options.JoinServerUrl}");
            sb.AppendLine($"token: \"{Escape(options.Token)}\"");
        }

        if (options.TlsSans.Count > 0)
        {
            sb.AppendLine("tls-san:");
            foreach (var san in options.TlsSans)
            {
                sb.AppendLine($"  - {san}");
            }
        }

        return sb.ToString();
    }

    public static string RenderAgent(AgentJoinOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.JoinServerUrl))
        {
            throw new ArgumentException("JoinServerUrl is required", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("Token is required", nameof(options));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"server: {options.JoinServerUrl}");
        sb.AppendLine($"token: \"{Escape(options.Token)}\"");
        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
