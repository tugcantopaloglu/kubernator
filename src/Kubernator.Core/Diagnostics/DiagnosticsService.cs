using System.Reflection;
using System.Runtime.InteropServices;
using Kubernator.Core.Containers;
using Kubernator.Core.Validation;
using Kubernator.Core.Vulnerabilities;

namespace Kubernator.Core.Diagnostics;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IProcessRunner runner;
    private readonly IContainerEngineProvider engineProvider;
    private readonly IVulnerabilityDatabase vulnDb;

    public DiagnosticsService(
        IProcessRunner runner,
        IContainerEngineProvider engineProvider,
        IVulnerabilityDatabase vulnDb)
    {
        this.runner = runner;
        this.engineProvider = engineProvider;
        this.vulnDb = vulnDb;
    }

    public async Task<DiagnosticReport> RunAsync(CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();

        checks.Add(await CheckContainerEngineAsync(ct));
        checks.Add(await CheckExternalToolAsync("kubectl", ["version", "--client=true", "--output=yaml"], ct,
            hint: "needed for kubernator validate (kind cluster apply)"));
        checks.Add(await CheckExternalToolAsync("kind", ["version"], ct,
            hint: "needed for kubernator validate (creates ephemeral cluster)"));
        checks.Add(CheckVulnDb());
        checks.Add(CheckHomeDirectoryWritable());

        var asm = typeof(DiagnosticsService).Assembly;
        var version = asm.GetName().Version?.ToString(3)
            ?? asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        return new DiagnosticReport
        {
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            DotNetRuntime = RuntimeInformation.FrameworkDescription,
            ToolVersion = version,
            Checks = checks
        };
    }

    private async Task<DiagnosticCheck> CheckContainerEngineAsync(CancellationToken ct)
    {
        try
        {
            var engine = await engineProvider.ResolveAsync(ct);
            var info = await engine.GetInfoAsync(ct);
            return new DiagnosticCheck
            {
                Name = "container engine",
                Status = DiagnosticStatus.Ok,
                Message = $"{info.Name} {info.Version} ({info.OperatingSystem}/{info.Architecture})"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = "container engine",
                Status = DiagnosticStatus.Fail,
                Message = ex.Message,
                Hint = "kubernator build / bundle / validate need a Docker-compatible engine on this host"
            };
        }
    }

    private async Task<DiagnosticCheck> CheckExternalToolAsync(
        string tool,
        IReadOnlyList<string> args,
        CancellationToken ct,
        string? hint = null)
    {
        try
        {
            var outcome = await runner.RunAsync(new ProcessInvocation
            {
                FileName = tool,
                Arguments = args,
                Timeout = TimeSpan.FromSeconds(10)
            }, ct);

            if (outcome.Ok)
            {
                var firstLine = FirstNonEmptyLine(outcome.StandardOutput) ?? FirstNonEmptyLine(outcome.StandardError) ?? "ok";
                return new DiagnosticCheck
                {
                    Name = tool,
                    Status = DiagnosticStatus.Ok,
                    Message = firstLine
                };
            }

            return new DiagnosticCheck
            {
                Name = tool,
                Status = DiagnosticStatus.Warn,
                Message = $"{tool} returned exit code {outcome.ExitCode}",
                Hint = hint
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = tool,
                Status = DiagnosticStatus.Warn,
                Message = $"not found on PATH ({ex.GetType().Name})",
                Hint = hint
            };
        }
    }

    private DiagnosticCheck CheckVulnDb()
    {
        try
        {
            var manifest = vulnDb.GetManifestAsync().GetAwaiter().GetResult();
            if (manifest is null)
            {
                return new DiagnosticCheck
                {
                    Name = "vulndb",
                    Status = DiagnosticStatus.Info,
                    Message = $"no manifest at {vulnDb.RootDirectory}",
                    Hint = "run kubernator vulndb update or vulndb import"
                };
            }
            return new DiagnosticCheck
            {
                Name = "vulndb",
                Status = DiagnosticStatus.Ok,
                Message = $"{manifest.Ecosystems.Count} ecosystems, updated {manifest.UpdatedAt:O}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = "vulndb",
                Status = DiagnosticStatus.Warn,
                Message = ex.Message
            };
        }
    }

    private static DiagnosticCheck CheckHomeDirectoryWritable()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".kubernator");
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".doctor-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new DiagnosticCheck
            {
                Name = "state directory",
                Status = DiagnosticStatus.Ok,
                Message = dir
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = "state directory",
                Status = DiagnosticStatus.Fail,
                Message = $"cannot write to {dir}: {ex.Message}",
                Hint = "kubernator stores keys, vulndb, and update artifacts under ~/.kubernator"
            };
        }
    }

    private static string? FirstNonEmptyLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0) return line;
        }
        return null;
    }
}
