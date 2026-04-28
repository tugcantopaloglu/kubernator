using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Analysis.Static;

public sealed class StaticWebAnalyzer : IAppAnalyzer
{
    public AppKind Handles => AppKind.StaticWeb;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = detection.SourcePath;

        var entry = new EntryPoint
        {
            Path = path,
            AssemblyName = Path.GetFileName(path),
            StartupCommand = "nginx",
            Arguments = ["-g", "daemon off;"]
        };

        var runtime = new RuntimeInfo
        {
            Name = "Nginx (static)",
            Version = null,
            Tfm = null,
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.Unknown,
            PublishMode = PublishMode.FrameworkDependent,
            FrameworkReferences = []
        };

        var network = new NetworkInfo
        {
            Ports = [8080],
            ListensHttp = true,
            ListensHttps = false,
            RequiresIngress = true
        };

        var deps = new DependencyInfo();

        return Task.FromResult(new AppDescriptor
        {
            SourcePath = path,
            Kind = AppKind.StaticWeb,
            Flavor = detection.Flavor,
            Runtime = runtime,
            Network = network,
            Dependencies = deps,
            EntryPoint = entry,
            EnvironmentHints = new Dictionary<string, string>(StringComparer.Ordinal),
            Warnings = detection.Warnings,
            DetectionConfidence = detection.Confidence
        });
    }
}
