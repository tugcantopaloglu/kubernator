using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Packaging;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class BundleCommand : AsyncCommand<BundleCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IBundleService bundleService;
    private readonly IContainerEngineProvider engineProvider;

    public BundleCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IBundleService bundleService,
        IContainerEngineProvider engineProvider)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.bundleService = bundleService;
        this.engineProvider = engineProvider;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <file>")]
        [Description("Path of the .kubpack bundle to produce.")]
        public string? OutputBundlePath { get; init; }

        [CommandOption("--name <image>")]
        [Description("Container image name.")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        [Description("Container image tag.")]
        public string? ImageTag { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Kubernetes namespace.")]
        public string? Namespace { get; init; }

        [CommandOption("--replicas <n>")]
        public int? Replicas { get; init; }

        [CommandOption("--no-sbom")]
        [Description("Skip SBOM generation.")]
        public bool NoSbom { get; init; }

        [CommandOption("--keep-scratch")]
        [Description("Keep the staging directory after bundling (for inspection).")]
        public bool KeepScratch { get; init; }

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

        IContainerEngine engine;
        try
        {
            engine = await engineProvider.ResolveAsync();
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 3;
        }

        var info = await engine.GetInfoAsync();
        AnsiConsole.MarkupLine($"[green]engine[/]   {info.Name} {info.Version} ({info.OperatingSystem}/{info.Architecture})");

        var bundlePath = settings.OutputBundlePath
            ?? System.IO.Path.Combine(Environment.CurrentDirectory, $"{plan.ImageName}-{plan.ImageTag}.kubpack");
        var scratchDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bundlePath) ?? ".", $".{plan.ImageName}-{plan.ImageTag}-scratch");

        var options = new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = scratchDir,
            IncludeSbom = !settings.NoSbom,
            KubernetesNamespace = settings.Namespace,
            Replicas = settings.Replicas ?? 1,
            KeepScratch = settings.KeepScratch
        };

        BundleResult result;
        try
        {
            result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Building bundle (build, save image, sbom, scripts, archive)",
                    async _ => await bundleService.CreateAsync(plan, options, engine));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]bundle failed:[/] {Markup.Escape(ex.Message)}");
            return 4;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]bundle[/]   {Markup.Escape(result.BundlePath)}");
        AnsiConsole.MarkupLine($"[green]size[/]     {result.BundleSizeBytes / 1024 / 1024} MB");
        AnsiConsole.MarkupLine($"[green]sha256[/]   {result.BundleSha256}");
        AnsiConsole.MarkupLine($"[green]images[/]   {result.Manifest.Images.Count}");
        AnsiConsole.MarkupLine($"[green]files[/]    {result.Manifest.Files.Count}");
        AnsiConsole.MarkupLine($"[green]ns[/]       {Markup.Escape(result.Manifest.KubernetesNamespace)}");

        return 0;
    }
}
