using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Helm;

internal static class HelmEmitter
{
    public static string ChartYaml(BuildPlan plan, HelmOptions options)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: v2");
        w.Line($"name: {YamlValue.String(SanitizeChartName(options.ChartName ?? plan.ImageName))}");
        w.Line($"version: {YamlValue.String(options.ChartVersion)}");
        w.Line($"appVersion: {YamlValue.String(plan.ImageTag)}");
        w.Line($"description: {YamlValue.String(options.Description ?? $"{plan.App.Kind} application packaged by kubernator")}");
        w.Line("type: application");
        w.Line("kubeVersion: \">=1.27.0\"");
        return w.ToString();
    }

    public static string ValuesYaml(BuildPlan plan, HelmOptions options)
    {
        var w = new IndentedTextWriter();
        w.Line("nameOverride: \"\"");
        w.Line("fullnameOverride: \"\"");
        w.Blank();
        w.Line("image:");
        w.Indent();
        w.Line($"repository: {YamlValue.String(plan.ImageName)}");
        w.Line($"tag: {YamlValue.String(plan.ImageTag)}");
        w.Line("pullPolicy: IfNotPresent");
        w.Outdent();
        w.Blank();
        w.Line($"replicaCount: {options.Replicas}");
        w.Blank();
        w.Line("resources:");
        w.Indent();
        w.Line("requests:");
        w.Indent();
        w.Line($"cpu: {YamlValue.String(options.CpuRequest)}");
        w.Line($"memory: {YamlValue.String(options.MemoryRequest)}");
        w.Outdent();
        w.Line("limits:");
        w.Indent();
        w.Line($"cpu: {YamlValue.String(options.CpuLimit)}");
        w.Line($"memory: {YamlValue.String(options.MemoryLimit)}");
        w.Outdent();
        w.Outdent();
        w.Blank();
        w.Line("securityContext:");
        w.Indent();
        w.Line("runAsNonRoot: true");
        w.Line($"runAsUser: {plan.Security.RunAsUser}");
        w.Line($"runAsGroup: {plan.Security.RunAsGroup}");
        w.Line($"fsGroup: {plan.Security.RunAsGroup}");
        w.Line("seccompProfile:");
        w.Indent();
        w.Line("type: RuntimeDefault");
        w.Outdent();
        w.Outdent();
        w.Blank();
        w.Line("containerSecurityContext:");
        w.Indent();
        w.Line("allowPrivilegeEscalation: false");
        w.Line("readOnlyRootFilesystem: true");
        w.Line("runAsNonRoot: true");
        w.Line($"runAsUser: {plan.Security.RunAsUser}");
        w.Line($"runAsGroup: {plan.Security.RunAsGroup}");
        w.Line("capabilities:");
        w.Indent();
        w.Line("drop:");
        w.Indent();
        w.Line("- ALL");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Blank();
        w.Line("service:");
        w.Indent();
        w.Line("enabled: true");
        w.Line("type: ClusterIP");
        var port = plan.ExposedPorts.Count > 0 ? plan.ExposedPorts[0] : 8080;
        w.Line($"port: {port}");
        w.Outdent();
        w.Blank();
        EmitIngressDefaults(w, plan, options, port);
        w.Blank();
        EmitAutoscalingDefaults(w, options.Scaling);
        w.Blank();
        EmitPdbDefaults(w, options.Scaling);
        w.Blank();
        w.Line("networkPolicy:");
        w.Indent();
        w.Line("enabled: true");
        w.Outdent();
        w.Blank();
        w.Line("healthProbe:");
        w.Indent();
        w.Line($"enabled: {(plan.Health is not null ? "true" : "false")}");
        w.Line($"path: {YamlValue.String(plan.Health?.HttpPath ?? "/health")}");
        w.Outdent();
        w.Blank();
        w.Line("env: {}");
        w.Blank();
        EmitCertManagerDefaults(w, options.Exposure);
        return w.ToString();
    }

    private static void EmitIngressDefaults(IndentedTextWriter w, BuildPlan plan, HelmOptions options, int port)
    {
        var exposure = options.Exposure;
        var enabled = exposure is not null;
        w.Line("ingress:");
        w.Indent();
        w.Line($"enabled: {(enabled ? "true" : "false")}");
        w.Line($"className: {YamlValue.String(exposure?.IngressClassName ?? "nginx")}");
        w.Line("annotations:");
        w.Indent();
        if (enabled && exposure!.RedirectHttpToHttps && exposure.TlsMode != TlsMode.None)
        {
            w.Line("nginx.ingress.kubernetes.io/ssl-redirect: \"true\"");
            w.Line("nginx.ingress.kubernetes.io/force-ssl-redirect: \"true\"");
        }
        w.Outdent();
        w.Line("hosts:");
        w.Indent();
        if (enabled)
        {
            foreach (var host in exposure!.AllHostnames)
            {
                w.Line($"- host: {YamlValue.String(host)}");
                w.Indent();
                w.Line("paths:");
                w.Indent();
                w.Line($"- path: {YamlValue.String(exposure.Path)}");
                w.Indent();
                w.Line("pathType: Prefix");
                w.Outdent();
                w.Outdent();
                w.Outdent();
            }
        }
        else
        {
            w.Line($"- host: app.example.com");
            w.Indent();
            w.Line("paths:");
            w.Indent();
            w.Line("- path: /");
            w.Indent();
            w.Line("pathType: Prefix");
            w.Outdent();
            w.Outdent();
            w.Outdent();
        }
        w.Outdent();
        w.Line("tls:");
        w.Indent();
        if (enabled && exposure!.TlsMode != TlsMode.None)
        {
            w.Line("- hosts:");
            w.Indent();
            w.Indent();
            foreach (var host in exposure.AllHostnames)
            {
                w.Line($"- {YamlValue.String(host)}");
            }
            w.Outdent();
            w.Line($"secretName: {YamlValue.String(exposure.TlsSecretName)}");
            w.Outdent();
        }
        w.Outdent();
        w.Line("tlsSecret:");
        w.Indent();
        w.Line($"create: false");
        w.Line($"name: {YamlValue.String(exposure?.TlsSecretName ?? "tls-cert")}");
        w.Line("cert: \"\"");
        w.Line("key: \"\"");
        w.Outdent();
        w.Outdent();
    }

    private static void EmitAutoscalingDefaults(IndentedTextWriter w, Generation.ScalingOptions? scaling)
    {
        w.Line("autoscaling:");
        w.Indent();
        var enabled = scaling?.HpaEnabled ?? false;
        w.Line($"enabled: {(enabled ? "true" : "false")}");
        w.Line($"minReplicas: {scaling?.HpaMinReplicas ?? 2}");
        w.Line($"maxReplicas: {scaling?.HpaMaxReplicas ?? 10}");
        if (scaling?.HpaTargetCpuUtilization is { } cpu)
        {
            w.Line($"targetCPUUtilizationPercentage: {cpu}");
        }
        else
        {
            w.Line("targetCPUUtilizationPercentage: 75");
        }
        if (scaling?.HpaTargetMemoryUtilization is { } memory)
        {
            w.Line($"targetMemoryUtilizationPercentage: {memory}");
        }
        else
        {
            w.Line("targetMemoryUtilizationPercentage: ~");
        }
        w.Outdent();
    }

    private static void EmitPdbDefaults(IndentedTextWriter w, Generation.ScalingOptions? scaling)
    {
        w.Line("podDisruptionBudget:");
        w.Indent();
        var enabled = scaling?.PdbEnabled ?? false;
        w.Line($"enabled: {(enabled ? "true" : "false")}");
        if (scaling?.PdbMinAvailable is { } min)
        {
            w.Line($"minAvailable: {min}");
        }
        else if (!string.IsNullOrEmpty(scaling?.PdbMinAvailablePercent))
        {
            w.Line($"minAvailable: {YamlValue.String(scaling.PdbMinAvailablePercent)}");
        }
        else if (scaling?.PdbMaxUnavailable is { } max)
        {
            w.Line($"maxUnavailable: {max}");
        }
        else if (!string.IsNullOrEmpty(scaling?.PdbMaxUnavailablePercent))
        {
            w.Line($"maxUnavailable: {YamlValue.String(scaling.PdbMaxUnavailablePercent)}");
        }
        else
        {
            w.Line("minAvailable: 1");
        }
        w.Outdent();
    }

    private static void EmitCertManagerDefaults(IndentedTextWriter w, ExposureOptions? exposure)
    {
        w.Line("certManager:");
        w.Indent();
        var enabled = exposure?.TlsMode == TlsMode.CertManager;
        w.Line($"enabled: {(enabled ? "true" : "false")}");
        w.Line("dnsNames:");
        w.Indent();
        if (enabled)
        {
            foreach (var host in exposure!.AllHostnames)
            {
                w.Line($"- {YamlValue.String(host)}");
            }
        }
        w.Outdent();
        w.Line("issuer:");
        w.Indent();
        w.Line($"kind: {YamlValue.String(exposure?.CertManagerIssuerKind ?? "ClusterIssuer")}");
        w.Line($"name: {YamlValue.String(exposure?.CertManagerIssuerName ?? "letsencrypt-prod")}");
        w.Outdent();
        w.Outdent();
    }

    private static string SanitizeChartName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name = new string(chars).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }
        return string.IsNullOrEmpty(name) ? "app" : name;
    }
}
