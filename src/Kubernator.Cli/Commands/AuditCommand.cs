using System.ComponentModel;
using Kubernator.Core.Audit;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class AuditCommand : Command<AuditCommand.Settings>
{
    private readonly ManifestAuditor auditor;

    public AuditCommand(ManifestAuditor auditor)
    {
        this.auditor = auditor;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory containing rendered Kubernetes manifests (usually <publish>/.kubernator/kubernetes).")]
        public string Directory { get; init; } = string.Empty;

        [CommandOption("--namespace <ns>")]
        [Description("Expected namespace; cross-namespace declarations are flagged as critical.")]
        public string? Namespace { get; init; }

        [CommandOption("--fail-on <severity>")]
        [Description("Exit non-zero when at least one finding has this severity or higher (info | warning | critical). Default: critical.")]
        public string FailOn { get; init; } = "critical";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Directory))
        {
            AnsiConsole.MarkupLine("[red]directory is required[/]");
            return 11;
        }
        var dir = Path.GetFullPath(settings.Directory);
        var result = auditor.AuditDirectory(dir, settings.Namespace);

        AnsiConsole.MarkupLine($"[green]inspected[/] {result.InspectedFiles.Count} file(s) in [cyan]{Markup.Escape(dir)}[/]");

        if (result.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]baseline OK[/]");
            return 0;
        }

        var table = new Table().AddColumn("code").AddColumn("severity").AddColumn("message").AddColumn("fix").Border(TableBorder.Rounded);
        foreach (var f in result.Findings.OrderByDescending(x => (int)x.Severity))
        {
            var sev = f.Severity switch
            {
                AuditSeverity.Critical => "[red]critical[/]",
                AuditSeverity.Warning => "[yellow]warning[/]",
                _ => "[grey]info[/]"
            };
            table.AddRow(Markup.Escape(f.Code), sev, Markup.Escape(f.Message), Markup.Escape(f.FixHint ?? ""));
        }
        AnsiConsole.Write(table);

        var threshold = settings.FailOn.ToLowerInvariant() switch
        {
            "info" => AuditSeverity.Info,
            "warning" => AuditSeverity.Warning,
            _ => AuditSeverity.Critical
        };
        var fail = result.Findings.Any(f => f.Severity >= threshold);
        return fail ? 14 : 0;
    }
}
