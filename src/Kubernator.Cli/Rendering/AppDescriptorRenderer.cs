using Kubernator.Core.Models;
using Spectre.Console;

namespace Kubernator.Cli.Rendering;

internal static class AppDescriptorRenderer
{
    public static void Render(AppDescriptor d)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(d.Kind.ToString())} · {Markup.Escape(d.Flavor.ToString())}[/]")
            .RuleStyle("green")
            .LeftJustified());

        var summary = new Grid().AddColumn().AddColumn();
        summary.AddRow("[grey]source[/]", Markup.Escape(d.SourcePath));
        summary.AddRow("[grey]confidence[/]", d.DetectionConfidence.ToString("P0", System.Globalization.CultureInfo.InvariantCulture));
        summary.AddRow("[grey]tfm[/]", Markup.Escape(d.Runtime.Tfm ?? "?"));
        summary.AddRow("[grey]runtime version[/]", Markup.Escape(d.Runtime.Version ?? "?"));
        summary.AddRow("[grey]rid[/]", Markup.Escape(d.Runtime.RuntimeIdentifier ?? "(framework-dependent)"));
        summary.AddRow("[grey]target os[/]", Markup.Escape(d.Runtime.TargetOs.ToString()));
        summary.AddRow("[grey]target arch[/]", Markup.Escape(d.Runtime.TargetArch.ToString()));
        summary.AddRow("[grey]publish mode[/]", Markup.Escape(d.Runtime.PublishMode.ToString()));
        summary.AddRow("[grey]frameworks[/]", string.Join(", ", d.Runtime.FrameworkReferences.Select(Markup.Escape)));
        AnsiConsole.Write(new Panel(summary)
        {
            Header = new PanelHeader("runtime"),
            Border = BoxBorder.Rounded
        });

        if (d.EntryPoint is not null)
        {
            var ep = new Grid().AddColumn().AddColumn();
            ep.AddRow("[grey]assembly[/]", Markup.Escape(d.EntryPoint.AssemblyName ?? "?"));
            ep.AddRow("[grey]command[/]", Markup.Escape(d.EntryPoint.StartupCommand ?? "?"));
            ep.AddRow("[grey]arguments[/]", string.Join(' ', d.EntryPoint.Arguments.Select(Markup.Escape)));
            ep.AddRow("[grey]path[/]", Markup.Escape(d.EntryPoint.Path));
            AnsiConsole.Write(new Panel(ep)
            {
                Header = new PanelHeader("entry point"),
                Border = BoxBorder.Rounded
            });
        }

        var net = new Grid().AddColumn().AddColumn();
        net.AddRow("[grey]ports[/]", d.Network.Ports.Count == 0 ? "[grey](none)[/]" : string.Join(", ", d.Network.Ports));
        net.AddRow("[grey]http[/]", d.Network.ListensHttp ? "[green]yes[/]" : "[grey]no[/]");
        net.AddRow("[grey]https[/]", d.Network.ListensHttps ? "[green]yes[/]" : "[grey]no[/]");
        net.AddRow("[grey]urls[/]", d.Network.Urls.Count == 0 ? "[grey](none)[/]" : string.Join(", ", d.Network.Urls.Select(Markup.Escape)));
        net.AddRow("[grey]ingress[/]", d.Network.RequiresIngress ? "[green]recommended[/]" : "[grey]not required[/]");
        AnsiConsole.Write(new Panel(net)
        {
            Header = new PanelHeader("network"),
            Border = BoxBorder.Rounded
        });

        var deps = new Grid().AddColumn().AddColumn();
        deps.AddRow("[grey]managed packages[/]", d.Dependencies.Managed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        deps.AddRow("[grey]native libs[/]", d.Dependencies.Native.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        deps.AddRow("[grey]requires icu[/]", d.Dependencies.RequiresIcu ? "[yellow]yes[/]" : "[green]no[/]");
        deps.AddRow("[grey]requires tzdata[/]", d.Dependencies.RequiresTimezone ? "[yellow]yes[/]" : "[green]no[/]");
        deps.AddRow("[grey]requires gdiplus[/]", d.Dependencies.RequiresGdiPlus ? "[red]yes[/]" : "[green]no[/]");
        AnsiConsole.Write(new Panel(deps)
        {
            Header = new PanelHeader("dependencies"),
            Border = BoxBorder.Rounded
        });

        if (d.Dependencies.Native.Count > 0)
        {
            var nativeTable = new Table().AddColumn("native dependency").Border(TableBorder.Rounded);
            foreach (var n in d.Dependencies.Native.Take(20))
            {
                nativeTable.AddRow(Markup.Escape(n.Name));
            }
            if (d.Dependencies.Native.Count > 20)
            {
                nativeTable.AddRow($"[grey]... and {d.Dependencies.Native.Count - 20} more[/]");
            }
            AnsiConsole.Write(nativeTable);
        }

        if (d.Warnings.Count > 0)
        {
            var rows = string.Join('\n', d.Warnings.Select(w => $"[yellow]![/]  {Markup.Escape(w)}"));
            AnsiConsole.Write(new Panel(rows)
            {
                Header = new PanelHeader("warnings"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            });
        }
    }
}
