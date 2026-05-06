using System.ComponentModel;
using Kubernator.Core.AirGapped;
using Kubernator.Core.Containers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class RehostCommand : AsyncCommand<RehostCommand.Settings>
{
    private readonly IImageBundleService bundleService;
    private readonly IContainerEngineProvider engineProvider;

    public RehostCommand(IImageBundleService bundleService, IContainerEngineProvider engineProvider)
    {
        this.bundleService = bundleService;
        this.engineProvider = engineProvider;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--bundle <dir>")]
        [Description("Directory containing the image .tar files and images.manifest.json (produced by `kubernator pull`).")]
        public string Bundle { get; init; } = string.Empty;

        [CommandOption("--registry <host>")]
        [Description("Target private registry host (e.g. registry.airgap.local:5000).")]
        public string Registry { get; init; } = string.Empty;

        [CommandOption("--namespace <ns>")]
        [Description("Optional namespace path under the target registry (e.g. infra/mirror).")]
        public string? RegistryNamespace { get; init; }

        [CommandOption("--manifests <dir>")]
        [Description("Optional manifests directory whose `image:` references will be rewritten in-place to the new registry.")]
        public string? ManifestsDirectory { get; init; }

        [CommandOption("--no-load")]
        [Description("Skip `docker load` step (use when the engine already has the images in cache).")]
        public bool NoLoad { get; init; }

        [CommandOption("--no-rewrite")]
        [Description("Do not rewrite manifest image references even if --manifests is provided.")]
        public bool NoRewrite { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Bundle))
            {
                return ValidationResult.Error("--bundle is required");
            }
            if (string.IsNullOrWhiteSpace(Registry))
            {
                return ValidationResult.Error("--registry is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var bundleDir = Path.GetFullPath(settings.Bundle);
        if (!Directory.Exists(bundleDir))
        {
            AnsiConsole.MarkupLine($"[red]bundle directory not found:[/] {Markup.Escape(bundleDir)}");
            return 31;
        }

        IContainerEngine engine;
        try
        {
            engine = await engineProvider.ResolveAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]no container engine available:[/] {Markup.Escape(ex.Message)}");
            return 32;
        }

        var manifest = await bundleService.ReadManifestAsync(bundleDir);
        if (manifest is null)
        {
            AnsiConsole.MarkupLine($"[red]images.manifest.json not found in[/] {Markup.Escape(bundleDir)}");
            return 33;
        }
        AnsiConsole.MarkupLine($"[grey]found[/]    {manifest.Images.Count} image(s) in bundle");
        AnsiConsole.MarkupLine($"[grey]target[/]   {Markup.Escape(settings.Registry)}{(string.IsNullOrEmpty(settings.RegistryNamespace) ? "" : "/" + settings.RegistryNamespace)}");

        var manifestsDir = string.IsNullOrWhiteSpace(settings.ManifestsDirectory)
            ? null
            : Path.GetFullPath(settings.ManifestsDirectory);

        var options = new ImageRehostOptions
        {
            BundleDirectory = bundleDir,
            TargetRegistry = settings.Registry,
            TargetNamespace = settings.RegistryNamespace,
            LoadBeforePush = !settings.NoLoad,
            RewriteManifestImages = !settings.NoRewrite,
            ManifestsDirectory = manifestsDir
        };

        ImageRehostResult result;
        try
        {
            result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Rehosting images", async ctx =>
                {
                    var progress = new Progress<string>(line =>
                    {
                        var trimmed = line.Length > 80 ? line[..80] : line;
                        ctx.Status(trimmed);
                    });
                    return await bundleService.RehostAsync(options, engine, progress);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]rehost failed:[/] {Markup.Escape(ex.Message)}");
            return 34;
        }

        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("source")
            .AddColumn("target")
            .Border(TableBorder.Rounded);
        foreach (var p in result.Pushed)
        {
            table.AddRow(Markup.Escape(p.SourceReference), Markup.Escape(p.TargetReference));
        }
        AnsiConsole.Write(table);

        if (result.RewrittenManifestFiles.Count > 0)
        {
            AnsiConsole.MarkupLine($"[green]rewrote[/] {result.RewrittenManifestFiles.Count} manifest file(s):");
            foreach (var f in result.RewrittenManifestFiles)
            {
                AnsiConsole.MarkupLine($"  · {Markup.Escape(f)}");
            }
        }

        if (result.Errors.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]errors:[/]");
            foreach (var e in result.Errors)
            {
                AnsiConsole.MarkupLine($"[red]![/] {Markup.Escape(e)}");
            }
            return 35;
        }
        AnsiConsole.MarkupLine($"[green]rehost ok[/]  {result.Pushed.Count} image(s) pushed to {Markup.Escape(settings.Registry)}");
        return 0;
    }
}
