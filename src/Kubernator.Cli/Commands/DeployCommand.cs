using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Deployment;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class DeployCommand : AsyncCommand<DeployCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;
    private readonly IClusterApplier applier;

    public DeployCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation,
        IClusterApplier applier)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
        this.applier = applier;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to a published application output. Omit when --list-contexts or --manifests-dir is used.")]
        public string? Path { get; init; }

        [CommandOption("--context <ctx>")]
        [Description("kubectl context to deploy into (default: current context).")]
        public string? Context { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Kubernetes namespace (default: default).")]
        public string Namespace { get; init; } = "default";

        [CommandOption("--manifests-dir <dir>")]
        [Description("Use a pre-generated manifests directory instead of regenerating from <path>.")]
        public string? ManifestsDirectory { get; init; }

        [CommandOption("--replicas <n>")]
        public int? Replicas { get; init; }

        [CommandOption("--dry-run")]
        [Description("Send to the API server with --dry-run=server (no changes persisted).")]
        public bool DryRun { get; init; }

        [CommandOption("--allow-production")]
        [Description("Required when --context name contains 'prod', 'production', or 'live'.")]
        public bool AllowProduction { get; init; }

        [CommandOption("--no-create-namespace")]
        [Description("Do not auto-create the namespace if it is missing.")]
        public bool NoCreateNamespace { get; init; }

        [CommandOption("--list-contexts")]
        [Description("Print available kubectl contexts and exit.")]
        public bool ListContexts { get; init; }

        public override ValidationResult Validate()
        {
            if (!ListContexts && string.IsNullOrWhiteSpace(Path) && string.IsNullOrWhiteSpace(ManifestsDirectory))
            {
                return ValidationResult.Error("path is required (or pass --manifests-dir / --list-contexts)");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.ListContexts)
        {
            var ctxs = await applier.ListContextsAsync();
            if (ctxs.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]no kubectl contexts found[/]");
                return 1;
            }
            foreach (var c in ctxs)
            {
                var marker = c.IsCurrent ? "[green]*[/]" : " ";
                var prod = c.LooksProduction ? " [yellow](looks production)[/]" : "";
                AnsiConsole.MarkupLine($"{marker} {Markup.Escape(c.Name)}{prod}");
            }
            return 0;
        }

        var path = string.IsNullOrWhiteSpace(settings.Path) ? null : System.IO.Path.GetFullPath(settings.Path);
        var ctxName = settings.Context;
        if (string.IsNullOrWhiteSpace(ctxName))
        {
            var contexts = await applier.ListContextsAsync();
            var current = contexts.FirstOrDefault(c => c.IsCurrent);
            if (current is null)
            {
                AnsiConsole.MarkupLine("[red]no current kubectl context — pass --context[/]");
                return 2;
            }
            ctxName = current.Name;
        }

        string manifestsDir;
        if (!string.IsNullOrWhiteSpace(settings.ManifestsDirectory))
        {
            manifestsDir = System.IO.Path.GetFullPath(settings.ManifestsDirectory);
            AnsiConsole.MarkupLine($"[grey]using manifests at[/] {Markup.Escape(manifestsDir)}");
        }
        else
        {
            if (path is null)
            {
                AnsiConsole.MarkupLine("[red]path is required when --manifests-dir is not set[/]");
                return 11;
            }
            var descriptor = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Analyzing [cyan]{Markup.Escape(path)}[/]", async _ => await analysis.AnalyzeAsync(path));

            var plan = strategy.Plan(descriptor);
            var output = System.IO.Path.Combine(path, ".kubernator", "deploy");
            manifestsDir = System.IO.Path.Combine(output, "kubernetes");

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Generating manifests", async _ =>
                {
                    await generation.GenerateAsync(plan, new GenerationOptions
                    {
                        OutputDirectory = output,
                        Namespace = settings.Namespace,
                        Replicas = settings.Replicas ?? 1,
                        OverwriteExisting = true
                    });
                });
            AnsiConsole.MarkupLine($"[green]generated[/] {Markup.Escape(manifestsDir)}");
        }

        if (ClusterContext.LooksLikeProduction(ctxName) && !settings.AllowProduction)
        {
            AnsiConsole.MarkupLine($"[red]refusing to deploy to '{Markup.Escape(ctxName)}'[/] — looks like production. pass [cyan]--allow-production[/] to proceed.");
            return 12;
        }

        AnsiConsole.MarkupLine($"[green]context[/]   {Markup.Escape(ctxName)}");
        AnsiConsole.MarkupLine($"[green]namespace[/] {Markup.Escape(settings.Namespace)}");
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]dry-run[/] (no changes will be applied)");
        }
        AnsiConsole.WriteLine();

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Deploying", async ctx =>
            {
                var progress = new Progress<string>(line => ctx.Status(line.Length > 80 ? line[..80] : line));
                return await applier.ApplyAsync(new DeployOptions
                {
                    ManifestsDirectory = manifestsDir,
                    Context = ctxName,
                    Namespace = settings.Namespace,
                    DryRun = settings.DryRun,
                    AllowProduction = settings.AllowProduction,
                    CreateNamespace = !settings.NoCreateNamespace
                }, progress);
            });

        AnsiConsole.WriteLine();
        if (result.Ok)
        {
            AnsiConsole.MarkupLine($"[green]deploy ok[/]  {result.AppliedResources.Count} line(s)");
            foreach (var line in result.AppliedResources.Take(20))
            {
                AnsiConsole.WriteLine(line);
            }
            return 0;
        }
        AnsiConsole.MarkupLine("[red]deploy failed[/]");
        foreach (var err in result.Errors)
        {
            AnsiConsole.MarkupLine($"[red]![/] {Markup.Escape(err)}");
        }
        return 13;
    }
}
