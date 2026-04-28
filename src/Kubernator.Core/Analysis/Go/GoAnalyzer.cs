using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Analysis.Go;

public sealed class GoAnalyzer : IAppAnalyzer
{
    public AppKind Handles => AppKind.Go;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = detection.SourcePath;
        var warnings = new List<string>(detection.Warnings);

        var binary = FindBinary(path);
        EntryPoint? entry = null;
        if (binary is not null)
        {
            entry = new EntryPoint
            {
                Path = binary,
                AssemblyName = Path.GetFileNameWithoutExtension(binary),
                StartupCommand = $"/app/{Path.GetFileName(binary)}",
                Arguments = []
            };
        }
        else
        {
            warnings.Add("no Go binary found at path; build a statically linked binary first");
        }

        var runtime = new RuntimeInfo
        {
            Name = "Go",
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.Unknown,
            PublishMode = PublishMode.SelfContained
        };

        return Task.FromResult(new AppDescriptor
        {
            SourcePath = path,
            Kind = AppKind.Go,
            Flavor = AppFlavor.GoBinary,
            Runtime = runtime,
            Network = new NetworkInfo(),
            Dependencies = new DependencyInfo(),
            EntryPoint = entry,
            EnvironmentHints = new Dictionary<string, string>(StringComparer.Ordinal),
            Warnings = warnings,
            DetectionConfidence = detection.Confidence
        });
    }

    private static string? FindBinary(string root)
    {
        Span<byte> magic = stackalloc byte[4];
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            using var stream = File.OpenRead(file);
            if (stream.Read(magic) != 4)
            {
                continue;
            }
            if (magic[0] == 0x7F && magic[1] == 0x45 && magic[2] == 0x4C && magic[3] == 0x46)
            {
                return file;
            }
        }
        return null;
    }
}
