using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tls;

namespace Kubernator.Core.Generation;

public sealed class GenerationService : IGenerationService
{
    public async Task<GenerationResult> GenerateAsync(
        BuildPlan plan,
        GenerationOptions options,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var manifestsDir = Path.Combine(options.OutputDirectory, "kubernetes");
        Directory.CreateDirectory(manifestsDir);

        var ns = options.Namespace ?? "default";
        var name = SanitizeKubernetesName(plan.ImageName);

        var written = new List<string>();

        await WriteAsync(options, Path.Combine(options.OutputDirectory, "Dockerfile"),
            DockerfileEmitter.Emit(plan), written, ct);

        await WriteAsync(options, Path.Combine(options.OutputDirectory, ".dockerignore"),
            DockerignoreEmitter.Emit(), written, ct);

        await WriteAsync(options, Path.Combine(manifestsDir, "deployment.yaml"),
            KubernetesEmitter.Deployment(plan, options, name, ns), written, ct);

        if (plan.ExposedPorts.Count > 0)
        {
            await WriteAsync(options, Path.Combine(manifestsDir, "service.yaml"),
                KubernetesEmitter.Service(plan, name, ns), written, ct);
        }

        await WriteAsync(options, Path.Combine(manifestsDir, "networkpolicy.yaml"),
            KubernetesEmitter.NetworkPolicy(plan, name, ns), written, ct);

        if (plan.Exposure is not null)
        {
            await WriteExposureAsync(plan, options, manifestsDir, name, ns, written, ct);
        }

        if (options.Scaling is { } scaling)
        {
            if (scaling.HpaEnabled)
            {
                await WriteAsync(options, Path.Combine(manifestsDir, "hpa.yaml"),
                    AutoscalingEmitter.Hpa(name, ns, scaling), written, ct);
            }
            if (scaling.PdbEnabled)
            {
                await WriteAsync(options, Path.Combine(manifestsDir, "pdb.yaml"),
                    AutoscalingEmitter.Pdb(name, ns, scaling), written, ct);
            }
        }

        return new GenerationResult
        {
            OutputDirectory = options.OutputDirectory,
            WrittenFiles = written
        };
    }

    private static async Task WriteExposureAsync(
        BuildPlan plan,
        GenerationOptions options,
        string manifestsDir,
        string name,
        string ns,
        List<string> written,
        CancellationToken ct)
    {
        var exposure = plan.Exposure!;

        var ingress = IngressEmitter.Ingress(plan, name, ns);
        await WriteAsync(options, Path.Combine(manifestsDir, "ingress.yaml"), ingress, written, ct);

        switch (exposure.TlsMode)
        {
            case TlsMode.None:
                return;

            case TlsMode.SelfSigned:
                {
                    var material = SelfSignedCertificateGenerator.Generate(
                        exposure.PrimaryHostname,
                        exposure.AdditionalHostnames);
                    var secret = IngressEmitter.TlsSecret(exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem);
                    await WriteAsync(options, Path.Combine(manifestsDir, "tls-secret.yaml"), secret, written, ct);
                    break;
                }

            case TlsMode.UserProvided:
                {
                    if (string.IsNullOrEmpty(exposure.UserCertificatePemPath) ||
                        string.IsNullOrEmpty(exposure.UserPrivateKeyPemPath))
                    {
                        throw new InvalidOperationException(
                            "TlsMode.UserProvided requires UserCertificatePemPath and UserPrivateKeyPemPath");
                    }
                    var material = SelfSignedCertificateGenerator.LoadFromFiles(
                        exposure.UserCertificatePemPath,
                        exposure.UserPrivateKeyPemPath);
                    var secret = IngressEmitter.TlsSecret(exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem);
                    await WriteAsync(options, Path.Combine(manifestsDir, "tls-secret.yaml"), secret, written, ct);
                    break;
                }

            case TlsMode.CertManager:
                {
                    var cert = IngressEmitter.CertManagerCertificate(plan, name, ns);
                    await WriteAsync(options, Path.Combine(manifestsDir, "certificate.yaml"), cert, written, ct);
                    break;
                }
        }
    }

    private static async Task WriteAsync(
        GenerationOptions options,
        string path,
        string content,
        List<string> written,
        CancellationToken ct)
    {
        if (!options.OverwriteExisting && File.Exists(path))
        {
            return;
        }
        await File.WriteAllTextAsync(path, content, ct);
        written.Add(path);
    }

    private static string SanitizeKubernetesName(string raw)
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
