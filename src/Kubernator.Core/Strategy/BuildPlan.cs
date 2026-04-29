using Kubernator.Core.Models;

namespace Kubernator.Core.Strategy;

public enum BuildStrategy
{
    CopyFromPublish,
    MultiStageFromSource
}

public sealed record BuildPlan
{
    public required AppDescriptor App { get; init; }
    public required BaseImage RuntimeImage { get; init; }
    public BaseImage? BuildImage { get; init; }
    public required BuildStrategy Strategy { get; init; }
    public required string ImageName { get; init; }
    public required string ImageTag { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyList<int> ExposedPorts { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
    public required string EntrypointCommand { get; init; }
    public required IReadOnlyList<string> EntrypointArguments { get; init; }
    public required HealthProbe? Health { get; init; }
    public required SecurityHardening Security { get; init; }
    public ExposureOptions? Exposure { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    public string FullImageReference => $"{ImageName}:{ImageTag}";
}

public sealed record HealthProbe
{
    public required HealthProbeKind Kind { get; init; }
    public string? HttpPath { get; init; }
    public int? Port { get; init; }
    public IReadOnlyList<string> ExecCommand { get; init; } = [];
}

public enum HealthProbeKind
{
    None,
    HttpGet,
    TcpSocket,
    Exec
}

public sealed record SecurityHardening
{
    public bool RunAsNonRoot { get; init; } = true;
    public long RunAsUser { get; init; }
    public long RunAsGroup { get; init; }
    public bool ReadOnlyRootFilesystem { get; init; } = true;
    public bool AllowPrivilegeEscalation { get; init; }
    public IReadOnlyList<string> DroppedCapabilities { get; init; } = ["ALL"];
    public IReadOnlyList<string> WritableMounts { get; init; } = ["/tmp"];
}
