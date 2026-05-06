using System.ComponentModel;
using Kubernator.Core.AirGapped;
using Kubernator.Core.Containers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class PullCommand : AsyncCommand<PullCommand.Settings>
{
    private readonly IImageBundleService bundleService;
    private readonly IContainerEngineProvider engineProvider;

    public PullCommand(IImageBundleService bundleService, IContainerEngineProvider engineProvider)
    {
        this.bundleService = bundleService;
        this.engineProvider = engineProvider;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<images>")]
        [Description("One or more image references to pull (e.g. nginx:1.27 redis:7.4).")]
        public string[] Images { get; init; } = [];

        [CommandOption("-o|--output <dir>")]
        [Description("Output directory for the image tarballs and manifest (default: ./images).")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--platform <p>")]
        [Description("Target platform (e.g. linux/amd64).")]
        public string? Platform { get; init; }

        [CommandOption("--force-pull")]
        [Description("Pull from registry even if a local copy already exists.")]
        public bool ForcePull { get; init; }

        [CommandOption("--combined")]
        [Description("Compress all .tar files + manifest into a single .tar.gz.")]
        public bool Combined { get; init; }

        public override ValidationResult Validate()
        {
            if (Images.Length == 0)
            {
                return ValidationResult.Error("at least one image reference is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = Path.GetFullPath(settings.OutputDirectory ?? Path.Combine(Environment.CurrentDirectory, "images"));
        AnsiConsole.MarkupLine($"[grey]output[/]   {Markup.Escape(output)}");

        IContainerEngine engine;
        try
        {
            engine = await engineProvider.ResolveAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]no container engine available:[/] {Markup.Escape(ex.Message)}");
            return 21;
        }

        var bundleOptions = new ImageBundleOptions
        {
            References = settings.Images,
            OutputDirectory = output,
            Platform = settings.Platform,
            ForcePull = settings.ForcePull,
            CombineIntoSingleArchive = settings.Combined
        };

        ImageBundleResult result;
        try
        {
            result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Pulling images", async ctx =>
                {
                    var progress = new Progress<string>(line =>
                    {
                        var trimmed = line.Length > 80 ? line[..80] : line;
                        ctx.Status(trimmed);
                    });
                    return await bundleService.PullAsync(bundleOptions, engine, progress);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]pull failed:[/] {Markup.Escape(ex.Message)}");
            return 22;
        }

        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("image")
            .AddColumn(new TableColumn("size").RightAligned())
            .AddColumn("sha256")
            .Border(TableBorder.Rounded);
        foreach (var entry in result.Manifest.Images)
        {
            table.AddRow(
                Markup.Escape(entry.Reference),
                FormatBytes(entry.SizeBytes),
                Markup.Escape(entry.Sha256[..12]));
        }
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"[green]manifest[/] {Markup.Escape(result.ManifestPath)}");
        if (result.CombinedArchivePath is not null)
        {
            AnsiConsole.MarkupLine($"[green]archive[/]  {Markup.Escape(result.CombinedArchivePath)}");
        }
        AnsiConsole.MarkupLine($"[green]images[/]   {result.Manifest.Images.Count} pulled");
        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024;
        const double mib = kib * 1024;
        const double gib = mib * 1024;
        return bytes switch
        {
            >= (long)gib => $"{bytes / gib:0.00} GiB",
            >= (long)mib => $"{bytes / mib:0.00} MiB",
            >= (long)kib => $"{bytes / kib:0.00} KiB",
            _ => $"{bytes} B"
        };
    }
}
