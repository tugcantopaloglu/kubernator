using System.Formats.Tar;
using System.IO.Compression;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Helm;

public sealed class HelmService : IHelmService
{
    public async Task<HelmGenerationResult> GenerateAsync(
        BuildPlan plan,
        HelmOptions options,
        CancellationToken ct = default)
    {
        var chartName = SanitizeChartName(options.ChartName ?? plan.ImageName);
        var chartDir = Path.Combine(options.OutputDirectory, chartName);
        var templatesDir = Path.Combine(chartDir, "templates");
        Directory.CreateDirectory(templatesDir);

        var written = new List<string>();

        await Write(Path.Combine(chartDir, "Chart.yaml"), HelmEmitter.ChartYaml(plan, options), written, ct);
        await Write(Path.Combine(chartDir, "values.yaml"), HelmEmitter.ValuesYaml(plan, options), written, ct);

        await Write(Path.Combine(templatesDir, "_helpers.tpl"), HelmTemplates.Helpers, written, ct);
        await Write(Path.Combine(templatesDir, "deployment.yaml"), HelmTemplates.Deployment, written, ct);
        await Write(Path.Combine(templatesDir, "service.yaml"), HelmTemplates.Service, written, ct);
        await Write(Path.Combine(templatesDir, "ingress.yaml"), HelmTemplates.Ingress, written, ct);
        await Write(Path.Combine(templatesDir, "hpa.yaml"), HelmTemplates.Hpa, written, ct);
        await Write(Path.Combine(templatesDir, "pdb.yaml"), HelmTemplates.Pdb, written, ct);
        await Write(Path.Combine(templatesDir, "networkpolicy.yaml"), HelmTemplates.NetworkPolicy, written, ct);
        await Write(Path.Combine(templatesDir, "tls-secret.yaml"), HelmTemplates.TlsSecret, written, ct);
        await Write(Path.Combine(templatesDir, "certificate.yaml"), HelmTemplates.CertManagerCertificate, written, ct);

        string? packagePath = null;
        if (options.Package)
        {
            packagePath = Path.Combine(options.OutputDirectory, $"{chartName}-{options.ChartVersion}.tgz");
            await PackageChartAsync(chartDir, packagePath, ct);
        }

        return new HelmGenerationResult
        {
            ChartDirectory = chartDir,
            WrittenFiles = written,
            PackageFile = packagePath
        };
    }

    private static async Task Write(string path, string content, List<string> written, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, content, ct);
        written.Add(path);
    }

    private static async Task PackageChartAsync(string chartDir, string outputTgz, CancellationToken ct)
    {
        await using var fileStream = File.Create(outputTgz);
        await using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
        await TarFile.CreateFromDirectoryAsync(chartDir, gzip, includeBaseDirectory: true, ct);
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
