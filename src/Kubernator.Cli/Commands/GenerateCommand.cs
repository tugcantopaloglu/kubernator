using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;

    public GenerateCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <dir>")]
        [Description("Output directory for generated files.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--name <image>")]
        [Description("Container image name (default: derived from assembly).")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        [Description("Container image tag (default: runtime version).")]
        public string? ImageTag { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Kubernetes namespace (default: default).")]
        public string? Namespace { get; init; }

        [CommandOption("--replicas <n>")]
        [Description("Replica count for the Deployment (default: 1).")]
        public int? Replicas { get; init; }

        [CommandOption("--no-overwrite")]
        [Description("Do not overwrite existing generated files.")]
        public bool NoOverwrite { get; init; }

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
            .StartAsync($"Analyzing [cyan]{Markup.Escape(path)}[/]", async _ => await analysis.AnalyzeAsync(path));

        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = settings.ImageName,
            ImageTag = settings.ImageTag
        });

        var output = settings.OutputDirectory ?? System.IO.Path.Combine(path, ".kubernator");
        var options = new GenerationOptions
        {
            OutputDirectory = output,
            Namespace = settings.Namespace,
            Replicas = settings.Replicas ?? 1,
            OverwriteExisting = !settings.NoOverwrite
        };

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Dockerfile and Kubernetes manifests", async _ => await generation.GenerateAsync(plan, options));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]base image[/]   {Markup.Escape(plan.RuntimeImage.DisplayName)} ([grey]{Markup.Escape(plan.RuntimeImage.Reference)}[/])");
        AnsiConsole.MarkupLine($"[green]image[/]        {Markup.Escape(plan.FullImageReference)}");
        AnsiConsole.MarkupLine($"[green]workdir[/]      {Markup.Escape(plan.WorkingDirectory)}");
        AnsiConsole.MarkupLine($"[green]user[/]         {plan.Security.RunAsUser}:{plan.Security.RunAsGroup}");
        AnsiConsole.WriteLine();

        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var file in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, file)));
        }
        AnsiConsole.Write(table);

        return 0;
    }
}
