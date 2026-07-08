using System.ComponentModel;
using Kubernator.Core.Updates;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly IUpdateService updates;

    public UpdateCommand(IUpdateService updates)
    {
        this.updates = updates;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[mode]")]
        [Description("check (default) or apply")]
        public string Mode { get; init; } = "check";

        [CommandOption("--source <url-or-path>")]
        [Description("Release manifest URL (https or file path). Defaults to KUBERNATOR_UPDATE_URL env var.")]
        public string? Source { get; init; }

        [CommandOption("--rid <rid>")]
        [Description("Override target runtime identifier (e.g., linux-x64, win-x64).")]
        public string? RuntimeIdentifier { get; init; }

        [CommandOption("--target-path <path>")]
        [Description("Update a binary at this path instead of the running executable.")]
        public string? TargetPath { get; init; }

        public override ValidationResult Validate()
        {
            var mode = Mode.ToLowerInvariant();
            if (mode is not ("check" or "apply"))
            {
                return ValidationResult.Error("mode must be 'check' or 'apply'");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var source = settings.Source ?? Environment.GetEnvironmentVariable("KUBERNATOR_UPDATE_URL");
        if (string.IsNullOrWhiteSpace(source))
        {
            AnsiConsole.MarkupLine("[red]--source is required (or set KUBERNATOR_UPDATE_URL)[/]");
            return 2;
        }

        var mode = settings.Mode.ToLowerInvariant();
        if (mode == "check")
        {
            try
            {
                var check = await updates.CheckAsync(source);
                AnsiConsole.MarkupLine($"[grey]current[/]    {Markup.Escape(check.CurrentVersion)}");
                AnsiConsole.MarkupLine($"[grey]latest[/]     {Markup.Escape(check.Manifest.Version)} (published {check.Manifest.PublishedAt:O})");
                if (check.UpgradeAvailable)
                {
                    AnsiConsole.MarkupLine("[green]upgrade available[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]up to date[/]");
                }
                var rids = string.Join(", ", check.Manifest.Artifacts.Select(a => a.RuntimeIdentifier));
                AnsiConsole.MarkupLine($"[grey]artifacts[/]  {Markup.Escape(rids)}");
                if (!string.IsNullOrEmpty(check.Manifest.Notes))
                {
                    AnsiConsole.MarkupLine($"[grey]notes[/]      {Markup.Escape(check.Manifest.Notes)}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]update check failed:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        var progress = new Progress<string>(line => AnsiConsole.MarkupLine($"[grey]update[/]    {Markup.Escape(line)}"));
        try
        {
            var result = await updates.ApplyAsync(source, settings.RuntimeIdentifier, settings.TargetPath, progress);
            AnsiConsole.MarkupLine($"[green]upgraded[/]   {Markup.Escape(result.NewExecutablePath)}");
            AnsiConsole.MarkupLine($"[grey]version[/]    {Markup.Escape(result.ToVersion)}");
            AnsiConsole.MarkupLine($"[grey]sha256[/]     {Markup.Escape(result.Sha256)}");
            AnsiConsole.MarkupLine($"[grey]previous[/]   {Markup.Escape(result.OldExecutablePath)}");
            AnsiConsole.MarkupLine("[yellow]restart kubernator to use the new version[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]update failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
