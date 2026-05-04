using System.ComponentModel;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class ValidateCommand : AsyncCommand<ValidateCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;
    private readonly IContainerEngineProvider engineProvider;
    private readonly IValidator validator;

    public ValidateCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation,
        IContainerEngineProvider engineProvider,
        IValidator validator)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
        this.engineProvider = engineProvider;
        this.validator = validator;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--cluster <name>")]
        public string ClusterName { get; init; } = "kubernator-test";

        [CommandOption("--reuse-cluster")]
        public bool ReuseCluster { get; init; }

        [CommandOption("--keep-cluster")]
        [Description("Do not delete the kind cluster after validation.")]
        public bool KeepCluster { get; init; }

        [CommandOption("--namespace <ns>")]
        public string Namespace { get; init; } = "default";

        [CommandOption("--name <image>")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        public string? ImageTag { get; init; }

        [CommandOption("--probe-path <path>")]
        public string? ProbePath { get; init; }

        [CommandOption("--probe-port <port>")]
        public int? ProbePort { get; init; }

        [CommandOption("--ready-timeout <seconds>")]
        public int? ReadyTimeoutSeconds { get; init; }

        [CommandOption("--no-build")]
        [Description("Skip image build, expect the image to already exist on the host engine.")]
        public bool NoBuild { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return Spectre.Console.ValidationResult.Error("path is required");
            }
            return Spectre.Console.ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Path);
        var descriptor = await analysis.AnalyzeAsync(path);

        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = settings.ImageName,
            ImageTag = settings.ImageTag
        });

        var scratch = System.IO.Path.Combine(path, ".kubernator", "validate");
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
        Directory.CreateDirectory(scratch);

        var genResult = await generation.GenerateAsync(plan, new GenerationOptions
        {
            OutputDirectory = scratch,
            Namespace = settings.Namespace
        });

        if (!settings.NoBuild)
        {
            var buildOk = await BuildImageAsync(plan, scratch, path);
            if (!buildOk)
            {
                return 16;
            }
        }

        var manifestsDir = System.IO.Path.Combine(scratch, "kubernetes");
        var deploymentName = SanitizeKubernetesName(plan.ImageName);
        var probePort = settings.ProbePort
            ?? plan.Health?.Port
            ?? (plan.ExposedPorts.Count > 0 ? plan.ExposedPorts[0] : (int?)null);
        var probePath = settings.ProbePath ?? plan.Health?.HttpPath;

        var options = new ValidationOptions
        {
            ManifestsDirectory = manifestsDir,
            ImageReference = plan.FullImageReference,
            DeploymentName = deploymentName,
            ClusterName = settings.ClusterName,
            Namespace = settings.Namespace,
            ReadyTimeout = settings.ReadyTimeoutSeconds is { } secs
                ? TimeSpan.FromSeconds(secs)
                : TimeSpan.FromMinutes(2),
            ReuseExistingCluster = settings.ReuseCluster,
            DeleteClusterOnComplete = !settings.KeepCluster,
            HttpProbePath = probePath,
            HttpProbePort = probePort
        };

        AnsiConsole.MarkupLine($"[green]image[/]    {Markup.Escape(plan.FullImageReference)}");
        AnsiConsole.MarkupLine($"[green]cluster[/]  {Markup.Escape(options.ClusterName)}");
        AnsiConsole.MarkupLine($"[green]manifests[/] {Markup.Escape(manifestsDir)}");

        var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));
        var result = await validator.ValidateAsync(options, progress);

        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("step")
            .AddColumn("ok")
            .AddColumn("duration")
            .AddColumn("error")
            .Border(TableBorder.Rounded);
        foreach (var step in result.Steps)
        {
            table.AddRow(
                Markup.Escape(step.Name),
                step.Ok ? "[green]yes[/]" : "[red]no[/]",
                step.Duration.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s",
                Markup.Escape(string.IsNullOrEmpty(step.Error) ? string.Empty : Truncate(step.Error.Trim(), 80)));
        }
        AnsiConsole.Write(table);

        if (result.ClusterStillRunning && settings.KeepCluster)
        {
            AnsiConsole.MarkupLine($"[yellow]cluster {options.ClusterName} kept; remove with[/] kind delete cluster --name {options.ClusterName}");
        }

        return result.Ok ? 0 : 17;
    }

    private async Task<bool> BuildImageAsync(BuildPlan plan, string scratch, string sourcePath)
    {
        IContainerEngine engine;
        try
        {
            engine = await engineProvider.ResolveAsync();
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return false;
        }

        var contextDir = System.IO.Path.Combine(scratch, "build-context");
        if (Directory.Exists(contextDir))
        {
            Directory.Delete(contextDir, recursive: true);
        }
        Directory.CreateDirectory(contextDir);

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            if (file.Contains(System.IO.Path.Combine(".kubernator", string.Empty), StringComparison.Ordinal))
            {
                continue;
            }
            var rel = System.IO.Path.GetRelativePath(sourcePath, file);
            var target = System.IO.Path.Combine(contextDir, rel);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        var dockerfilePath = System.IO.Path.Combine(contextDir, "Dockerfile");
        File.Copy(System.IO.Path.Combine(scratch, "Dockerfile"), dockerfilePath, overwrite: true);
        var dockerignoreSrc = System.IO.Path.Combine(scratch, ".dockerignore");
        if (File.Exists(dockerignoreSrc))
        {
            File.Copy(dockerignoreSrc, System.IO.Path.Combine(contextDir, ".dockerignore"), overwrite: true);
        }
        await Task.CompletedTask;

        try
        {
            await foreach (var line in engine.BuildAsync(new BuildContext
            {
                ContextDirectory = contextDir,
                DockerfilePath = dockerfilePath,
                ImageName = plan.ImageName,
                ImageTag = plan.ImageTag
            }))
            {
                AnsiConsole.WriteLine(line);
            }
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]build failed:[/] {Markup.Escape(ex.Message)}");
            return false;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string SanitizeKubernetesName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name = new string(chars).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }
        return string.IsNullOrEmpty(name) ? "app" : name;
    }
}
