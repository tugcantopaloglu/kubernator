using Kubernator.Core.Strategy;

namespace Kubernator.Core.Generation.Emitters;

internal static class IngressEmitter
{
    public static string Ingress(BuildPlan plan, string name, string ns)
    {
        if (plan.Exposure is null)
        {
            throw new InvalidOperationException("plan has no Exposure configured");
        }
        var exposure = plan.Exposure;
        var port = exposure.OverridePort ?? (plan.ExposedPorts.Count > 0 ? plan.ExposedPorts[0] : 80);
        var w = new IndentedTextWriter();
        w.Line("apiVersion: networking.k8s.io/v1");
        w.Line("kind: Ingress");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Outdent();
        if (exposure.RedirectHttpToHttps)
        {
            w.Line("annotations:");
            w.Indent();
            w.Line($"nginx.ingress.kubernetes.io/ssl-redirect: {YamlValue.String("true")}");
            w.Line($"nginx.ingress.kubernetes.io/force-ssl-redirect: {YamlValue.String("true")}");
            w.Outdent();
        }
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line($"ingressClassName: {YamlValue.String(exposure.IngressClassName)}");
        if (exposure.TlsMode != TlsMode.None)
        {
            w.Line("tls:");
            w.Indent();
            w.Line("- hosts:");
            w.Indent();
            foreach (var host in exposure.AllHostnames)
            {
                w.Line($"- {YamlValue.String(host)}");
            }
            w.Outdent();
            w.Line($"secretName: {YamlValue.String(exposure.TlsSecretName)}");
            w.Outdent();
        }
        w.Line("rules:");
        w.Indent();
        foreach (var host in exposure.AllHostnames)
        {
            w.Line($"- host: {YamlValue.String(host)}");
            w.Indent();
            w.Line("http:");
            w.Indent();
            w.Line("paths:");
            w.Indent();
            w.Line($"- path: {YamlValue.String(exposure.Path)}");
            w.Indent();
            w.Line("pathType: Prefix");
            w.Line("backend:");
            w.Indent();
            w.Line("service:");
            w.Indent();
            w.Line($"name: {YamlValue.String(name)}");
            w.Line("port:");
            w.Indent();
            w.Line($"number: {port}");
            w.Outdent();
            w.Outdent();
            w.Outdent();
            w.Outdent();
            w.Outdent();
            w.Outdent();
            w.Outdent();
        }
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string TlsSecret(string secretName, string ns, string certificatePem, string privateKeyPem)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: v1");
        w.Line("kind: Secret");
        w.Line("type: kubernetes.io/tls");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(secretName)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Outdent();
        w.Line("data:");
        w.Indent();
        w.Line($"tls.crt: {Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(certificatePem))}");
        w.Line($"tls.key: {Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(privateKeyPem))}");
        w.Outdent();
        return w.ToString();
    }

    public static string CertManagerCertificate(BuildPlan plan, string name, string ns)
    {
        if (plan.Exposure is null || plan.Exposure.TlsMode != TlsMode.CertManager)
        {
            throw new InvalidOperationException("plan does not request a cert-manager certificate");
        }
        var exposure = plan.Exposure;
        var w = new IndentedTextWriter();
        w.Line("apiVersion: cert-manager.io/v1");
        w.Line("kind: Certificate");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line($"secretName: {YamlValue.String(exposure.TlsSecretName)}");
        w.Line("dnsNames:");
        w.Indent();
        foreach (var host in exposure.AllHostnames)
        {
            w.Line($"- {YamlValue.String(host)}");
        }
        w.Outdent();
        w.Line("issuerRef:");
        w.Indent();
        w.Line($"kind: {YamlValue.String(exposure.CertManagerIssuerKind)}");
        w.Line($"name: {YamlValue.String(exposure.CertManagerIssuerName ?? "letsencrypt-prod")}");
        w.Line("group: cert-manager.io");
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }
}
