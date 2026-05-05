using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Core.Containers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class BuildCommand : AsyncCommand<BuildCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;
    private readonly IContainerEngineProvider engineProvider;

    public BuildCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation,
        IContainerEngineProvider engineProvider)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
        this.engineProvider = engineProvider;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <dir>")]
        [Description("Output directory for generated files (default: <path>/.kubernator).")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--name <image>")]
        [Description("Container image name.")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        [Description("Container image tag.")]
        public string? ImageTag { get; init; }

        [CommandOption("--no-build")]
        [Description("Generate files but skip the container build.")]
        public bool NoBuild { get; init; }

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
        var options = new GenerationOptions { OutputDirectory = output };

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Dockerfile and Kubernetes manifests", async _ => await generation.GenerateAsync(plan, options));

        AnsiConsole.MarkupLine($"[green]generated[/] {result.WrittenFiles.Count} files in [cyan]{Markup.Escape(result.OutputDirectory)}[/]");

        if (settings.NoBuild)
        {
            AnsiConsole.MarkupLine("[grey]--no-build set, skipping container build.[/]");
            return 0;
        }

        var dockerfilePath = System.IO.Path.Combine(output, "Dockerfile");
        var contextDir = path;

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

        var stagingDir = System.IO.Path.Combine(output, "build-context");
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
        Directory.CreateDirectory(stagingDir);

        await CopyDirectoryAsync(contextDir, stagingDir, excludeRoot: output);
        File.Copy(dockerfilePath, System.IO.Path.Combine(stagingDir, "Dockerfile"), overwrite: true);
        var dockerignoreSrc = System.IO.Path.Combine(output, ".dockerignore");
        if (File.Exists(dockerignoreSrc))
        {
            File.Copy(dockerignoreSrc, System.IO.Path.Combine(stagingDir, ".dockerignore"), overwrite: true);
        }

        var buildCtx = new BuildContext
        {
            ContextDirectory = stagingDir,
            DockerfilePath = System.IO.Path.Combine(stagingDir, "Dockerfile"),
            ImageName = plan.ImageName,
            ImageTag = plan.ImageTag
        };

        AnsiConsole.MarkupLine($"[green]building[/] [cyan]{Markup.Escape(plan.FullImageReference)}[/]");
        AnsiConsole.WriteLine();

        try
        {
            await foreach (var line in engine.BuildAsync(buildCtx))
            {
                AnsiConsole.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]build failed:[/] {Markup.Escape(ex.Message)}");
            return 4;
        }

        AnsiConsole.WriteLine();
        var image = await engine.GetImageAsync(plan.FullImageReference);
        if (image is not null)
        {
            AnsiConsole.MarkupLine($"[green]done[/]    {Markup.Escape(plan.FullImageReference)} ([grey]{image.SizeBytes / 1024 / 1024} MB[/])");
        }

        return 0;
    }

    private static async Task CopyDirectoryAsync(string source, string destination, string? excludeRoot = null)
    {
        await Task.Run(() =>
        {
            var excludeFull = excludeRoot is null ? null : System.IO.Path.GetFullPath(excludeRoot);
            var sourceFull = System.IO.Path.GetFullPath(source);

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                if (IsUnderneath(System.IO.Path.GetFullPath(dir), excludeFull))
                {
                    continue;
                }
                var target = dir.Replace(source, destination, StringComparison.Ordinal);
                Directory.CreateDirectory(target);
            }
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (IsUnderneath(System.IO.Path.GetFullPath(file), excludeFull))
                {
                    continue;
                }
                var target = file.Replace(source, destination, StringComparison.Ordinal);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        });
    }

    private static bool IsUnderneath(string candidate, string? root)
    {
        if (root is null)
        {
            return false;
        }
        var normalized = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(normalized + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalized + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase);
    }
}
