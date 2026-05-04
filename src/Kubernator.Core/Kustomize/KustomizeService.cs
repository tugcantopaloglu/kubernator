using Kubernator.Core.Generation;
using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tls;

namespace Kubernator.Core.Kustomize;

public sealed class KustomizeService : IKustomizeService
{
    public async Task<KustomizeResult> GenerateAsync(BuildPlan plan, KustomizeOptions options, CancellationToken ct = default)
    {
        var baseDir = Path.Combine(options.OutputDirectory, "base");
        Directory.CreateDirectory(baseDir);
        var written = new List<string>();
        var ns = options.BaseNamespace ?? "default";
        var name = SanitizeName(plan.ImageName);

        var resources = new List<string>();

        await Write(Path.Combine(baseDir, "deployment.yaml"),
            KubernetesEmitter.Deployment(plan, BuildGenerationOptions(options), name, ns), written, ct);
        resources.Add("deployment.yaml");

        if (plan.ExposedPorts.Count > 0)
        {
            await Write(Path.Combine(baseDir, "service.yaml"),
                KubernetesEmitter.Service(plan, name, ns), written, ct);
            resources.Add("service.yaml");
        }

        await Write(Path.Combine(baseDir, "networkpolicy.yaml"),
            KubernetesEmitter.NetworkPolicy(plan, name, ns), written, ct);
        resources.Add("networkpolicy.yaml");

        if (plan.Exposure is not null)
        {
            await Write(Path.Combine(baseDir, "ingress.yaml"),
                IngressEmitter.Ingress(plan, name, ns), written, ct);
            resources.Add("ingress.yaml");

            switch (plan.Exposure.TlsMode)
            {
                case TlsMode.SelfSigned:
                    {
                        var material = SelfSignedCertificateGenerator.Generate(
                            plan.Exposure.PrimaryHostname,
                            plan.Exposure.AdditionalHostnames);
                        await Write(Path.Combine(baseDir, "tls-secret.yaml"),
                            IngressEmitter.TlsSecret(plan.Exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem),
                            written, ct);
                        resources.Add("tls-secret.yaml");
                        break;
                    }
                case TlsMode.UserProvided:
                    {
                        if (string.IsNullOrEmpty(plan.Exposure.UserCertificatePemPath) ||
                            string.IsNullOrEmpty(plan.Exposure.UserPrivateKeyPemPath))
                        {
                            throw new InvalidOperationException(
                                "TlsMode.UserProvided requires UserCertificatePemPath and UserPrivateKeyPemPath");
                        }
                        var material = SelfSignedCertificateGenerator.LoadFromFiles(
                            plan.Exposure.UserCertificatePemPath,
                            plan.Exposure.UserPrivateKeyPemPath);
                        await Write(Path.Combine(baseDir, "tls-secret.yaml"),
                            IngressEmitter.TlsSecret(plan.Exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem),
                            written, ct);
                        resources.Add("tls-secret.yaml");
                        break;
                    }
                case TlsMode.CertManager:
                    await Write(Path.Combine(baseDir, "certificate.yaml"),
                        IngressEmitter.CertManagerCertificate(plan, name, ns), written, ct);
                    resources.Add("certificate.yaml");
                    break;
            }
        }

        if (options.Scaling is { } scaling)
        {
            if (scaling.HpaEnabled)
            {
                await Write(Path.Combine(baseDir, "hpa.yaml"),
                    AutoscalingEmitter.Hpa(name, ns, scaling), written, ct);
                resources.Add("hpa.yaml");
            }
            if (scaling.PdbEnabled)
            {
                await Write(Path.Combine(baseDir, "pdb.yaml"),
                    AutoscalingEmitter.Pdb(name, ns, scaling), written, ct);
                resources.Add("pdb.yaml");
            }
        }

        await Write(Path.Combine(baseDir, "kustomization.yaml"),
            KustomizeEmitter.BaseKustomization(name, ns, plan, resources), written, ct);

        foreach (var overlay in options.Overlays)
        {
            var overlayDir = Path.Combine(options.OutputDirectory, "overlays", SanitizeName(overlay));
            Directory.CreateDirectory(overlayDir);
            await Write(Path.Combine(overlayDir, "kustomization.yaml"),
                KustomizeEmitter.OverlayKustomization(overlay, name, plan, options), written, ct);
        }

        return new KustomizeResult
        {
            BaseDirectory = baseDir,
            WrittenFiles = written
        };
    }

    private static GenerationOptions BuildGenerationOptions(KustomizeOptions options) => new()
    {
        OutputDirectory = options.OutputDirectory,
        Namespace = options.BaseNamespace,
        Replicas = options.Replicas,
        CpuRequest = options.CpuRequest,
        CpuLimit = options.CpuLimit,
        MemoryRequest = options.MemoryRequest,
        MemoryLimit = options.MemoryLimit,
        Scaling = options.Scaling
    };

    private static async Task Write(string path, string content, List<string> written, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, content, ct);
        written.Add(path);
    }

    private static string SanitizeName(string raw)
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
