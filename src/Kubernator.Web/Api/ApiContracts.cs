using Kubernator.Core.Audit;
using Kubernator.Core.Diagnostics;
using Kubernator.Core.Models;

namespace Kubernator.Web.Api;

public sealed record HealthResponse
{
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required long UptimeSeconds { get; init; }
}

public sealed record VersionResponse
{
    public required string Version { get; init; }
    public required string Os { get; init; }
    public required string Architecture { get; init; }
    public required string Framework { get; init; }
}

public sealed record DetectRequest
{
    public required string Path { get; init; }
}

public sealed record DetectResponse
{
    public required string Path { get; init; }
    public required IReadOnlyList<DetectionResultDto> Results { get; init; }
}

public sealed record DetectionResultDto
{
    public required string Kind { get; init; }
    public required string Flavor { get; init; }
    public required double Confidence { get; init; }
    public required string SourcePath { get; init; }
    public required IReadOnlyList<string> Signals { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public static DetectionResultDto From(DetectionResult result) => new()
    {
        Kind = result.Kind.ToString(),
        Flavor = result.Flavor.ToString(),
        Confidence = result.Confidence,
        SourcePath = result.SourcePath,
        Signals = result.Signals,
        Warnings = result.Warnings
    };
}

public sealed record AnalyzeRequest
{
    public required string Path { get; init; }
}

public sealed record AnalyzeResponse
{
    public required string SourcePath { get; init; }
    public required string Kind { get; init; }
    public required string Flavor { get; init; }
    public required double DetectionConfidence { get; init; }
    public required RuntimeInfoDto Runtime { get; init; }
    public required NetworkInfoDto Network { get; init; }
    public required DependencyInfoDto Dependencies { get; init; }
    public EntryPointDto? EntryPoint { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public static AnalyzeResponse From(AppDescriptor d) => new()
    {
        SourcePath = d.SourcePath,
        Kind = d.Kind.ToString(),
        Flavor = d.Flavor.ToString(),
        DetectionConfidence = d.DetectionConfidence,
        Runtime = RuntimeInfoDto.From(d.Runtime),
        Network = NetworkInfoDto.From(d.Network),
        Dependencies = DependencyInfoDto.From(d.Dependencies),
        EntryPoint = d.EntryPoint is null ? null : EntryPointDto.From(d.EntryPoint),
        Warnings = d.Warnings
    };
}

public sealed record RuntimeInfoDto
{
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Tfm { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public required string TargetOs { get; init; }
    public required string TargetArch { get; init; }
    public required string PublishMode { get; init; }
    public required IReadOnlyList<string> FrameworkReferences { get; init; }

    public static RuntimeInfoDto From(RuntimeInfo r) => new()
    {
        Name = r.Name,
        Version = r.Version,
        Tfm = r.Tfm,
        RuntimeIdentifier = r.RuntimeIdentifier,
        TargetOs = r.TargetOs.ToString(),
        TargetArch = r.TargetArch.ToString(),
        PublishMode = r.PublishMode.ToString(),
        FrameworkReferences = r.FrameworkReferences
    };
}

public sealed record NetworkInfoDto
{
    public required IReadOnlyList<int> Ports { get; init; }
    public required IReadOnlyList<string> Urls { get; init; }
    public required bool ListensHttp { get; init; }
    public required bool ListensHttps { get; init; }
    public required bool RequiresIngress { get; init; }

    public static NetworkInfoDto From(NetworkInfo n) => new()
    {
        Ports = n.Ports,
        Urls = n.Urls,
        ListensHttp = n.ListensHttp,
        ListensHttps = n.ListensHttps,
        RequiresIngress = n.RequiresIngress
    };
}

public sealed record DependencyInfoDto
{
    public required IReadOnlyList<string> Managed { get; init; }
    public required IReadOnlyList<string> Native { get; init; }
    public required bool RequiresIcu { get; init; }
    public required bool RequiresTimezone { get; init; }
    public required bool RequiresGdiPlus { get; init; }

    public static DependencyInfoDto From(DependencyInfo d) => new()
    {
        Managed = d.Managed.Select(m => $"{m.Name} {m.Version}").ToArray(),
        Native = d.Native.Select(n => n.Origin is null ? n.Name : $"{n.Name} ({n.Origin})").ToArray(),
        RequiresIcu = d.RequiresIcu,
        RequiresTimezone = d.RequiresTimezone,
        RequiresGdiPlus = d.RequiresGdiPlus
    };
}

public sealed record EntryPointDto
{
    public required string Path { get; init; }
    public string? AssemblyName { get; init; }
    public string? StartupCommand { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }

    public static EntryPointDto From(EntryPoint e) => new()
    {
        Path = e.Path,
        AssemblyName = e.AssemblyName,
        StartupCommand = e.StartupCommand,
        Arguments = e.Arguments
    };
}

public sealed record AuditRequest
{
    public required string Directory { get; init; }
    public string? ExpectedNamespace { get; init; }
}

public sealed record AuditResponse
{
    public required bool Pass { get; init; }
    public required IReadOnlyList<AuditFindingDto> Findings { get; init; }
    public required IReadOnlyList<string> InspectedFiles { get; init; }

    public static AuditResponse From(ManifestAuditResult r) => new()
    {
        Pass = r.Pass,
        Findings = r.Findings.Select(AuditFindingDto.From).ToArray(),
        InspectedFiles = r.InspectedFiles
    };
}

public sealed record AuditFindingDto
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public string? FixHint { get; init; }

    public static AuditFindingDto From(AuditFinding f) => new()
    {
        Severity = f.Severity.ToString(),
        Code = f.Code,
        Message = f.Message,
        FilePath = f.FilePath,
        FixHint = f.FixHint
    };
}

public sealed record DiagnosticsResponse
{
    public required bool Ok { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
    public required string DotNetRuntime { get; init; }
    public required string ToolVersion { get; init; }
    public required IReadOnlyList<DiagnosticCheckDto> Checks { get; init; }

    public static DiagnosticsResponse From(DiagnosticReport r) => new()
    {
        Ok = r.Ok,
        OperatingSystem = r.OperatingSystem,
        Architecture = r.Architecture,
        DotNetRuntime = r.DotNetRuntime,
        ToolVersion = r.ToolVersion,
        Checks = r.Checks.Select(c => new DiagnosticCheckDto
        {
            Name = c.Name,
            Status = c.Status.ToString(),
            Message = c.Message,
            Hint = c.Hint
        }).ToArray()
    };
}

public sealed record DiagnosticCheckDto
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public string? Hint { get; init; }
}
