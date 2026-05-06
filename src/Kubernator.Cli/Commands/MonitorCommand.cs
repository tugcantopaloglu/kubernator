using System.ComponentModel;
using Kubernator.Core.Deployment;
using Kubernator.Core.Monitoring;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class MonitorCommand : AsyncCommand<MonitorCommand.Settings>
{
    private readonly IClusterMonitor monitor;
    private readonly IClusterApplier applier;

    public MonitorCommand(IClusterMonitor monitor, IClusterApplier applier)
    {
        this.monitor = monitor;
        this.applier = applier;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--context <ctx>")]
        [Description("kubectl context to inspect (default: current context).")]
        public string? Context { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Filter pods/ingress/networkpolicies/services to a single namespace (default: all).")]
        public string? Namespace { get; init; }

        [CommandOption("--watch <seconds>")]
        [Description("Refresh every N seconds (Ctrl+C to exit). Omit for a single snapshot.")]
        public int? Watch { get; init; }

        [CommandOption("--no-metrics")]
        [Description("Skip metrics-server queries (kubectl top).")]
        public bool NoMetrics { get; init; }

        [CommandOption("--no-pods")]
        public bool NoPods { get; init; }

        [CommandOption("--no-ingress")]
        public bool NoIngress { get; init; }

        [CommandOption("--no-netpol")]
        public bool NoNetworkPolicies { get; init; }

        [CommandOption("--no-services")]
        public bool NoServices { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var ctxName = settings.Context;
        if (string.IsNullOrWhiteSpace(ctxName))
        {
            var contexts = await applier.ListContextsAsync();
            var current = contexts.FirstOrDefault(c => c.IsCurrent);
            if (current is null)
            {
                AnsiConsole.MarkupLine("[red]no current kubectl context — pass --context[/]");
                return 41;
            }
            ctxName = current.Name;
        }

        var options = new ClusterMonitorOptions
        {
            Context = ctxName,
            Namespace = settings.Namespace,
            IncludeMetrics = !settings.NoMetrics,
            IncludePods = !settings.NoPods,
            IncludeIngress = !settings.NoIngress,
            IncludeNetworkPolicies = !settings.NoNetworkPolicies,
            IncludeServices = !settings.NoServices
        };

        if (settings.Watch is null)
        {
            var snapshot = await CaptureAsync(options);
            Render(snapshot);
            return snapshot.Warnings.Count > 0 && snapshot.Nodes.Count == 0 ? 42 : 0;
        }

        var seconds = Math.Max(1, settings.Watch.Value);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        AnsiConsole.MarkupLine($"[grey]watching every {seconds}s — press Ctrl+C to exit[/]");

        try
        {
            while (!cts.IsCancellationRequested)
            {
                AnsiConsole.Clear();
                var snapshot = await CaptureAsync(options, cts.Token);
                Render(snapshot);
                AnsiConsole.MarkupLine($"[grey]captured at {snapshot.CapturedAt:HH:mm:ss} · next refresh in {seconds}s[/]");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        return 0;
    }

    private async Task<ClusterSnapshot> CaptureAsync(ClusterMonitorOptions options, CancellationToken ct = default)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Querying cluster ({options.Context})", async _ => await monitor.GetSnapshotAsync(options, ct));
    }

    private static void Render(ClusterSnapshot snapshot)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]context[/]   {Markup.Escape(snapshot.Context)}");
        if (snapshot.ApiVersion is not null)
        {
            AnsiConsole.MarkupLine($"[green]apiserver[/] {Markup.Escape(snapshot.ApiVersion)}");
        }
        AnsiConsole.MarkupLine($"[green]nodes[/]     {snapshot.ReadyNodes}/{snapshot.Nodes.Count} ready");
        AnsiConsole.MarkupLine($"[green]pods[/]      {snapshot.RunningPods} running, {snapshot.FailedPods} failed (of {snapshot.Pods.Count})");
        AnsiConsole.MarkupLine($"[green]metrics[/]   {(snapshot.MetricsServerAvailable ? "[green]available[/]" : "[yellow]unavailable[/]")}");
        AnsiConsole.WriteLine();

        if (snapshot.Nodes.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded)
                .AddColumn("node")
                .AddColumn("status")
                .AddColumn("roles")
                .AddColumn("kubelet")
                .AddColumn("cpu/mem (alloc)")
                .AddColumn("cpu/mem (use)")
                .AddColumn("age");
            foreach (var n in snapshot.Nodes.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var statusColor = string.Equals(n.Status, "Ready", StringComparison.Ordinal) ? "green" : "red";
                var alloc = $"{n.Allocatable.Cpu} / {n.Allocatable.Memory}";
                var use = n.Usage is null ? "—" : $"{n.Usage.Cpu} / {n.Usage.Memory}";
                t.AddRow(
                    Markup.Escape(n.Name),
                    $"[{statusColor}]{Markup.Escape(n.Status)}[/]",
                    Markup.Escape(string.Join(",", n.Roles)),
                    Markup.Escape(n.KubeletVersion),
                    Markup.Escape(alloc),
                    Markup.Escape(use),
                    FormatAge(n.Age));
            }
            AnsiConsole.Write(new Rule("[bold]nodes[/]").LeftJustified());
            AnsiConsole.Write(t);
        }

        if (snapshot.Pods.Count > 0)
        {
            var failed = snapshot.Pods.Where(p => !string.Equals(p.Phase, "Running", StringComparison.Ordinal)
                && !string.Equals(p.Phase, "Succeeded", StringComparison.Ordinal)).ToList();
            if (failed.Count > 0)
            {
                var t = new Table().Border(TableBorder.Rounded)
                    .AddColumn("namespace")
                    .AddColumn("pod")
                    .AddColumn("phase")
                    .AddColumn("ready")
                    .AddColumn("restarts")
                    .AddColumn("node")
                    .AddColumn("age");
                foreach (var p in failed.Take(50))
                {
                    var color = p.Phase switch
                    {
                        "Failed" or "Unknown" => "red",
                        "Pending" => "yellow",
                        _ => "white"
                    };
                    t.AddRow(
                        Markup.Escape(p.Namespace),
                        Markup.Escape(p.Name),
                        $"[{color}]{Markup.Escape(p.Phase)}[/]",
                        $"{p.ContainersReady}/{p.ContainersTotal}",
                        p.Restarts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Markup.Escape(p.NodeName),
                        FormatAge(p.Age));
                }
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]troubled pods ({failed.Count})[/]").LeftJustified());
                AnsiConsole.Write(t);
            }
        }

        if (snapshot.Ingresses.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded)
                .AddColumn("namespace")
                .AddColumn("ingress")
                .AddColumn("class")
                .AddColumn("hosts")
                .AddColumn("addresses")
                .AddColumn("tls");
            foreach (var ing in snapshot.Ingresses.OrderBy(x => x.Namespace).ThenBy(x => x.Name))
            {
                t.AddRow(
                    Markup.Escape(ing.Namespace),
                    Markup.Escape(ing.Name),
                    Markup.Escape(string.IsNullOrEmpty(ing.IngressClass) ? "<default>" : ing.IngressClass),
                    Markup.Escape(string.Join(", ", ing.Hosts)),
                    Markup.Escape(ing.Addresses.Count == 0 ? "—" : string.Join(", ", ing.Addresses)),
                    ing.TlsHosts.Count > 0 ? "[green]✓[/]" : "—");
            }
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold]ingresses[/]").LeftJustified());
            AnsiConsole.Write(t);
        }

        if (snapshot.NetworkPolicies.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded)
                .AddColumn("namespace")
                .AddColumn("policy")
                .AddColumn("types")
                .AddColumn("age");
            foreach (var p in snapshot.NetworkPolicies.OrderBy(x => x.Namespace).ThenBy(x => x.Name))
            {
                t.AddRow(
                    Markup.Escape(p.Namespace),
                    Markup.Escape(p.Name),
                    Markup.Escape(string.Join(",", p.PolicyTypes)),
                    FormatAge(p.Age));
            }
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold]network policies[/]").LeftJustified());
            AnsiConsole.Write(t);
        }

        if (snapshot.Warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]warnings:[/]");
            foreach (var w in snapshot.Warnings)
            {
                AnsiConsole.MarkupLine($"  · {Markup.Escape(w)}");
            }
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 0) return "—";
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h";
        if (age.TotalMinutes >= 1) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalSeconds}s";
    }
}
