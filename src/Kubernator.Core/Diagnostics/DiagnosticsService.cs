using System.Reflection;
using System.Runtime.InteropServices;
using Kubernator.Core.Containers;
using Kubernator.Core.Validation;
using Kubernator.Core.Vault;
using Kubernator.Core.Vulnerabilities;

namespace Kubernator.Core.Diagnostics;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IProcessRunner runner;
    private readonly IContainerEngineProvider engineProvider;
    private readonly IVulnerabilityDatabase vulnDb;
    private readonly IKeyVault vault;

    public DiagnosticsService(
        IProcessRunner runner,
        IContainerEngineProvider engineProvider,
        IVulnerabilityDatabase vulnDb,
        IKeyVault vault)
    {
        this.runner = runner;
        this.engineProvider = engineProvider;
        this.vulnDb = vulnDb;
        this.vault = vault;
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
        checks.Add(await CheckVaultAsync(ct));
        checks.Add(CheckAuthAccount());
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

    private async Task<DiagnosticCheck> CheckVaultAsync(CancellationToken ct)
    {
        try
        {
            var entries = await vault.ListAsync(ct);
            return new DiagnosticCheck
            {
                Name = "key vault",
                Status = DiagnosticStatus.Ok,
                Message = entries.Count == 0 ? $"empty ({vault.RootDirectory})" : $"{entries.Count} entry/ies at {vault.RootDirectory}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = "key vault",
                Status = DiagnosticStatus.Warn,
                Message = ex.Message
            };
        }
    }

    private static DiagnosticCheck CheckAuthAccount()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        var path = Path.Combine(home, "auth", "account.json");
        if (!File.Exists(path))
        {
            return new DiagnosticCheck
            {
                Name = "web auth account",
                Status = DiagnosticStatus.Info,
                Message = "not configured",
                Hint = "open the web UI; first request lands on /auth/setup"
            };
        }
        try
        {
            var size = new FileInfo(path).Length;
            return new DiagnosticCheck
            {
                Name = "web auth account",
                Status = DiagnosticStatus.Ok,
                Message = $"configured · {size} B at {path}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Name = "web auth account",
                Status = DiagnosticStatus.Warn,
                Message = ex.Message
            };
        }
    }

    private static DiagnosticCheck CheckHomeDirectoryWritable()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        var dir = home;
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
