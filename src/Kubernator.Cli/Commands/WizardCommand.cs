using Kubernator.Cli.Rendering;
using Kubernator.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class WizardCommand : AsyncCommand
{
    private readonly IAnalysisService analysis;

    public WizardCommand(IAnalysisService analysis)
    {
        this.analysis = analysis;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        AnsiConsole.Write(new Rule("[bold cyan]kubernator[/]").RuleStyle("cyan").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Containerize published applications for Kubernetes, with air-gapped delivery.[/]");
        AnsiConsole.WriteLine();

        var path = AnsiConsole.Ask<string>("Path to the application:", Directory.GetCurrentDirectory());
        var resolved = Path.GetFullPath(path);

        if (!Directory.Exists(resolved) && !File.Exists(resolved))
        {
            AnsiConsole.MarkupLine($"[red]not found:[/] {Markup.Escape(resolved)}");
            return 1;
        }

        try
        {
            var descriptor = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Analyzing [cyan]{Markup.Escape(resolved)}[/]", async _ => await analysis.AnalyzeAsync(resolved));

            AppDescriptorRenderer.Render(descriptor);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Next steps will be available once[/] [yellow]M2[/] [grey]ships (build, generate, validate, bundle).[/]");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }
    }
}
