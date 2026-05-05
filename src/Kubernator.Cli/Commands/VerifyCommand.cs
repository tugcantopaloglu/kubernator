using System.ComponentModel;
using System.IO.Compression;
using Kubernator.Core.Audit;
using Kubernator.Core.Packaging;
using Kubernator.Core.Packaging.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class VerifyCommand : AsyncCommand<VerifyCommand.Settings>
{
    private readonly IBundleService bundleService;
    private readonly ICosignSigner signer;
    private readonly InstallScriptAuditor scriptAuditor;

    public VerifyCommand(IBundleService bundleService, ICosignSigner signer, InstallScriptAuditor scriptAuditor)
    {
        this.bundleService = bundleService;
        this.signer = signer;
        this.scriptAuditor = scriptAuditor;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<bundle>")]
        [Description("Path to a .kubpack bundle to verify.")]
        public string Bundle { get; init; } = string.Empty;

        [CommandOption("--signature <path>")]
        [Description("Detached signature path (default: <bundle>.sig if present).")]
        public string? SignaturePath { get; init; }

        [CommandOption("--pubkey <path>")]
        [Description("Public key path (default: <bundle>.pub or cosign.pub next to bundle).")]
        public string? PublicKeyPath { get; init; }

        [CommandOption("--require-signature")]
        [Description("Fail if no signature is present alongside the bundle.")]
        public bool RequireSignature { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Bundle))
            {
                return ValidationResult.Error("bundle path is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Bundle);

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Verifying [cyan]{Markup.Escape(path)}[/]", async _ => await bundleService.VerifyAsync(path));

        if (result.Manifest is not null)
        {
            AnsiConsole.MarkupLine($"[green]tool[/]      {Markup.Escape(result.Manifest.Tool)} {Markup.Escape(result.Manifest.ToolVersion)}");
            AnsiConsole.MarkupLine($"[green]created[/]   {result.Manifest.CreatedAt:O}");
            AnsiConsole.MarkupLine($"[green]app[/]       {Markup.Escape(result.Manifest.App.Name)} {Markup.Escape(result.Manifest.App.Version)}");
            AnsiConsole.MarkupLine($"[green]images[/]    {result.Manifest.Images.Count}");
            AnsiConsole.MarkupLine($"[green]files[/]     {result.Manifest.Files.Count}");
        }

        var integrityOk = result.Ok;
        if (!integrityOk)
        {
            foreach (var err in result.Errors)
            {
                AnsiConsole.MarkupLine($"[red]![/]  {Markup.Escape(err)}");
            }
            return 5;
        }
        AnsiConsole.MarkupLine("[green]integrity OK[/]");

        var scriptResult = AuditInstallScript(path);
        if (scriptResult is not null)
        {
            if (scriptResult.Findings.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]install.sh OK[/]");
            }
            else
            {
                foreach (var f in scriptResult.Findings.OrderByDescending(x => (int)x.Severity))
                {
                    var sev = f.Severity switch
                    {
                        AuditSeverity.Critical => "[red]critical[/]",
                        AuditSeverity.Warning => "[yellow]warning[/]",
                        _ => "[grey]info[/]"
                    };
                    AnsiConsole.MarkupLine($"  {sev} [grey]{Markup.Escape(f.Code)}[/] {Markup.Escape(f.Message)}");
                }
                if (!scriptResult.Pass)
                {
                    AnsiConsole.MarkupLine("[red]install.sh has critical findings[/]");
                    return 5;
                }
            }
        }

        var sigPath = settings.SignaturePath ?? path + ".sig";
        var pubPath = settings.PublicKeyPath
            ?? (File.Exists(path + ".pub") ? path + ".pub"
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? ".", "cosign.pub"));

        if (!File.Exists(sigPath) || !File.Exists(pubPath))
        {
            if (settings.RequireSignature)
            {
                AnsiConsole.MarkupLine("[red]signature required but missing[/]");
                return 5;
            }
            AnsiConsole.MarkupLine("[grey]signature not present (skipped); use --require-signature to enforce[/]");
            return 0;
        }

        var sigResult = await signer.VerifyBlobAsync(path, sigPath, pubPath);
        if (sigResult.Valid)
        {
            AnsiConsole.MarkupLine($"[green]signature OK[/]  ({Markup.Escape(System.IO.Path.GetFileName(pubPath))})");
            return 0;
        }
        AnsiConsole.MarkupLine($"[red]signature invalid:[/] {Markup.Escape(sigResult.Error ?? "unknown error")}");
        return 5;
    }

    private ManifestAuditResult? AuditInstallScript(string bundlePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("scripts/install.sh", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            var tmp = Path.Combine(Path.GetTempPath(), $"kubernator-install-{Guid.NewGuid():N}.sh");
            entry.ExtractToFile(tmp, overwrite: true);
            try
            {
                return scriptAuditor.AuditFile(tmp);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
