using System.Runtime.InteropServices;
using Kubernator.Core.Updates;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class VersionCommand : Command<VersionCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[green]kubernator[/] {KubernatorVersion.Current}");
        AnsiConsole.MarkupLine($"[grey]runtime[/]    {Markup.Escape(RuntimeInformation.FrameworkDescription)}");
        AnsiConsole.MarkupLine($"[grey]platform[/]   {UpdateService.CurrentRuntimeIdentifier()}");
        return 0;
    }
}
