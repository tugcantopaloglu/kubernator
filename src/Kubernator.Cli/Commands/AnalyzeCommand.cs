using System.ComponentModel;
using System.Text.Json;
using Kubernator.Cli.Rendering;
using Kubernator.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class AnalyzeCommand : AsyncCommand<AnalyzeCommand.Settings>
{
    private readonly IAnalysisService analysis;

    public AnalyzeCommand(IAnalysisService analysis)
    {
        this.analysis = analysis;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output or source tree.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--json")]
        [Description("Emit machine-readable JSON instead of a table.")]
        public bool Json { get; init; }

        [CommandOption("--output <file>")]
        [Description("Write the JSON report to a file.")]
        public string? OutputFile { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return ValidationResult.Error("path is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Path);

        var descriptor = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Analyzing [cyan]{path}[/]", async _ => await analysis.AnalyzeAsync(path));

        if (settings.Json || settings.OutputFile is not null)
        {
            var json = JsonSerializer.Serialize(descriptor, JsonOptions.Pretty);
            if (settings.OutputFile is not null)
            {
                await File.WriteAllTextAsync(settings.OutputFile, json);
                AnsiConsole.MarkupLine($"[green]wrote[/] {Markup.Escape(settings.OutputFile)}");
            }
            else
            {
                AnsiConsole.WriteLine(json);
            }
            return 0;
        }

        AppDescriptorRenderer.Render(descriptor);
        return 0;
    }
}
