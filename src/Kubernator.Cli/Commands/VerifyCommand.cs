using System.ComponentModel;
using Kubernator.Core.Packaging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class VerifyCommand : AsyncCommand<VerifyCommand.Settings>
{
    private readonly IBundleService bundleService;

    public VerifyCommand(IBundleService bundleService)
    {
        this.bundleService = bundleService;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<bundle>")]
        [Description("Path to a .kubpack bundle to verify.")]
        public string Bundle { get; init; } = string.Empty;

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Bundle))
            {
                return ValidationResult.Error("bundle path is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Bundle);

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Verifying [cyan]{Markup.Escape(path)}[/]", async _ => await bundleService.VerifyAsync(path));

        if (result.Manifest is not null)
        {
            AnsiConsole.MarkupLine($"[green]tool[/]      {Markup.Escape(result.Manifest.Tool)} {Markup.Escape(result.Manifest.ToolVersion)}");
            AnsiConsole.MarkupLine($"[green]created[/]   {result.Manifest.CreatedAt:O}");
            AnsiConsole.MarkupLine($"[green]app[/]       {Markup.Escape(result.Manifest.App.Name)} {Markup.Escape(result.Manifest.App.Version)}");
            AnsiConsole.MarkupLine($"[green]images[/]    {result.Manifest.Images.Count}");
            AnsiConsole.MarkupLine($"[green]files[/]     {result.Manifest.Files.Count}");
        }

        if (result.Ok)
        {
            AnsiConsole.MarkupLine("[green]bundle integrity OK[/]");
            return 0;
        }

        foreach (var err in result.Errors)
        {
            AnsiConsole.MarkupLine($"[red]![/]  {Markup.Escape(err)}");
        }
        return 5;
    }
}
