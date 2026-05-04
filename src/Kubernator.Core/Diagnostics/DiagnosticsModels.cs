namespace Kubernator.Core.Diagnostics;

public enum DiagnosticStatus
{
    Ok,
    Warn,
    Fail,
    Info
}

public sealed record DiagnosticCheck
{
    public required string Name { get; init; }
    public required DiagnosticStatus Status { get; init; }
    public required string Message { get; init; }
    public string? Hint { get; init; }
}

public sealed record DiagnosticReport
{
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
    public required string DotNetRuntime { get; init; }
    public required string ToolVersion { get; init; }
    public required IReadOnlyList<DiagnosticCheck> Checks { get; init; }

    public bool Ok => !Checks.Any(c => c.Status == DiagnosticStatus.Fail);
}
