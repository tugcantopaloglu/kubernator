using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;

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

        return new GenerationResult
        {
            OutputDirectory = options.OutputDirectory,
            WrittenFiles = written
        };
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
