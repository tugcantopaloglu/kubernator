using Kubernator.Core.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    private readonly IDiagnosticsService diagnostics;

    public DoctorCommand(IDiagnosticsService diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public sealed class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var report = await diagnostics.RunAsync();

        AnsiConsole.MarkupLine($"[grey]os[/]        {Markup.Escape(report.OperatingSystem)} ({report.Architecture})");
        AnsiConsole.MarkupLine($"[grey]runtime[/]   {Markup.Escape(report.DotNetRuntime)}");
        AnsiConsole.MarkupLine($"[grey]tool[/]      kubernator {report.ToolVersion}");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("status")
            .AddColumn("check")
            .AddColumn("message")
            .AddColumn("hint");

        foreach (var c in report.Checks)
        {
            var (label, color) = c.Status switch
            {
                DiagnosticStatus.Ok => ("ok", "green"),
                DiagnosticStatus.Warn => ("warn", "yellow"),
                DiagnosticStatus.Fail => ("fail", "red"),
                _ => ("info", "grey")
            };
            table.AddRow(
                $"[{color}]{label}[/]",
                Markup.Escape(c.Name),
                Markup.Escape(c.Message),
                c.Hint is null ? "[grey]-[/]" : Markup.Escape(c.Hint));
        }
        AnsiConsole.Write(table);

        return report.Ok ? 0 : 1;
    }
}
