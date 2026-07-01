using System.ComponentModel;
using System.Globalization;
using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Artifacts;
using Kubernator.Core.ClusterProvisioning.Discovery;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
using Kubernator.Core.Deployment;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class ClusterCommand : AsyncCommand<ClusterCommand.Settings>
{
    private readonly IClusterArtifactBundleService artifactService;
    private readonly IClusterProvisioningService provisioningService;
    private readonly ClusterUpgradePlanner upgradePlanner;
    private readonly ClusterTopologyDiscoverer discoverer;
    private readonly IClusterApplier applier;

    public ClusterCommand(
        IClusterArtifactBundleService artifactService,
        IClusterProvisioningService provisioningService,
        ClusterUpgradePlanner upgradePlanner,
        ClusterTopologyDiscoverer discoverer,
        IClusterApplier applier)
    {
        this.artifactService = artifactService;
        this.provisioningService = provisioningService;
        this.upgradePlanner = upgradePlanner;
        this.discoverer = discoverer;
        this.applier = applier;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<action>")]
        [Description("Action: pull | trust-host | install | upgrade | status | discover")]
        public string Action { get; init; } = string.Empty;

        [CommandOption("-o|--output <path>")]
        [Description("Output directory for `pull`.")]
        public string? Output { get; init; }

        [CommandOption("--pack <path>")]
        [Description("After `pull`, also pack the bundle directory into a single .tar.gz archive at this path.")]
        public string? Pack { get; init; }

        [CommandOption("--distro <name>")]
        [DefaultValue("rke2")]
        [Description("Distribution to provision or pull artifacts for. Only 'rke2' is implemented today.")]
        public string Distro { get; init; } = "rke2";

        [CommandOption("--version <version>")]
        [Description("Distro version for `pull` (e.g. v1.30.4+rke2r1).")]
        public string? Version { get; init; }

        [CommandOption("--arch <archs>")]
        [DefaultValue("amd64")]
        [Description("Comma-separated architectures for `pull` (amd64,arm64).")]
        public string Arch { get; init; } = "amd64";

        [CommandOption("--kubectl")]
        [DefaultValue(true)]
        [Description("Include a kubectl binary in the pulled bundle.")]
        public bool Kubectl { get; init; } = true;

        [CommandOption("--helm")]
        [Description("Include a helm binary in the pulled bundle.")]
        public bool Helm { get; init; }

        [CommandOption("--k9s")]
        [Description("Include a k9s binary in the pulled bundle.")]
        public bool K9s { get; init; }

        [CommandOption("--helm-version <version>")]
        [DefaultValue("v3.16.2")]
        public string HelmVersion { get; init; } = "v3.16.2";

        [CommandOption("--k9s-version <version>")]
        [DefaultValue("v0.32.5")]
        public string K9sVersion { get; init; } = "v0.32.5";

        [CommandOption("--include-selinux")]
        [Description("Also download the rke2-selinux policy RPM for RHEL-family nodes (best effort).")]
        public bool IncludeSelinux { get; init; }

        [CommandOption("--selinux-version <version>")]
        public string? SelinuxVersion { get; init; }

        [CommandOption("--topology <path>")]
        [Description("Path to a cluster topology JSON file, for `install` | `upgrade` | `status`.")]
        public string? Topology { get; init; }

        [CommandOption("--to-version <version>")]
        [Description("Target distro version for `upgrade`.")]
        public string? ToVersion { get; init; }

        [CommandOption("--allow-production")]
        [Description("Allow `install` / `upgrade` against a cluster name that looks like production.")]
        public bool AllowProduction { get; init; }

        [CommandOption("--host <host>")]
        [Description("Target host for `trust-host`.")]
        public string? Host { get; init; }

        [CommandOption("--port <port>")]
        [DefaultValue(22)]
        [Description("SSH port, for `trust-host` and applied to every node for `discover`.")]
        public int Port { get; init; } = 22;

        [CommandOption("--user <user>")]
        [Description("SSH username, for `trust-host` and applied to every node for `discover`.")]
        public string? User { get; init; }

        [CommandOption("--confirm")]
        [Description("Persist the fingerprint shown by `trust-host` (without this flag it is only printed).")]
        public bool Confirm { get; init; }

        [CommandOption("--context <ctx>")]
        [Description("kubectl context to discover from, for `discover` (default: current context).")]
        public string? Context { get; init; }

        [CommandOption("--cluster-name <name>")]
        [Description("Cluster name to record in the discovered topology, for `discover`.")]
        public string? ClusterName { get; init; }

        [CommandOption("--ssh-key-vault-id <id>")]
        [Description("Vault entry id for the SSH private key applied to every discovered node, for `discover`.")]
        public string? SshKeyVaultId { get; init; }

        [CommandOption("--ssh-key-path <path>")]
        [Description("Path to an SSH private key applied to every discovered node, for `discover`.")]
        public string? SshKeyPath { get; init; }

        [CommandOption("--fixed-registration-address <addrs>")]
        [Description("Comma-separated stable HA registration address(es), for `discover`.")]
        public string? FixedRegistrationAddresses { get; init; }

        [CommandOption("--bundle-path <path>")]
        [Description("Local artifact bundle path to record in the discovered topology, for `discover` (fill in later if not yet pulled).")]
        public string? BundlePath { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Action))
            {
                return ValidationResult.Error("action is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        switch (settings.Action.ToLowerInvariant())
        {
            case "pull": return await PullAsync(settings);
            case "trust-host": return await TrustHostAsync(settings);
            case "install": return await InstallAsync(settings);
            case "upgrade": return await UpgradeAsync(settings);
            case "status": return await StatusAsync(settings);
            case "discover": return await DiscoverAsync(settings);
            default:
                AnsiConsole.MarkupLine($"[red]unknown action:[/] {Markup.Escape(settings.Action)}");
                AnsiConsole.MarkupLine("[grey]use one of: pull, trust-host, install, upgrade, status, discover[/]");
                return 12;
        }
    }

    private async Task<int> PullAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Output))
        {
            AnsiConsole.MarkupLine("[red]--output is required[/]");
            return 13;
        }
        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            AnsiConsole.MarkupLine("[red]--version is required[/]");
            return 13;
        }
        if (!TryParseDistro(settings.Distro, out var distro))
        {
            AnsiConsole.MarkupLine($"[red]unsupported distro:[/] {Markup.Escape(settings.Distro)}");
            return 13;
        }
        if (distro is not (DistroKind.Rke2 or DistroKind.K3s or DistroKind.KubeadmNative))
        {
            AnsiConsole.MarkupLine($"[red]pulling artifacts for distro '{distro}' is not implemented yet — only 'rke2', 'k3s', and 'kubeadm' are supported[/]");
            return 13;
        }

        var archs = settings.Arch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new ClusterArtifactPullOptions
        {
            OutputDirectory = Path.GetFullPath(settings.Output),
            Distro = distro,
            Version = settings.Version,
            Architectures = archs,
            IncludeKubectl = settings.Kubectl,
            IncludeHelm = settings.Helm,
            IncludeK9s = settings.K9s,
            HelmVersion = settings.HelmVersion,
            K9sVersion = settings.K9sVersion,
            IncludeSelinuxPolicy = settings.IncludeSelinux,
            SelinuxPolicyVersion = settings.SelinuxVersion
        };

        var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));
        var manifest = await artifactService.PullAsync(options, progress);

        var table = new Table().AddColumn("kind").AddColumn("arch").AddColumn("path").AddColumn("size").Border(TableBorder.Rounded);
        foreach (var entry in manifest.Entries)
        {
            table.AddRow(entry.Kind, entry.Arch ?? "-", entry.RelativePath, entry.SizeBytes.ToString(CultureInfo.InvariantCulture));
        }
        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(settings.Pack))
        {
            var archivePath = await artifactService.PackAsync(options.OutputDirectory, Path.GetFullPath(settings.Pack), progress);
            AnsiConsole.MarkupLine($"[green]packed[/] {Markup.Escape(archivePath)}");
        }

        return 0;
    }

    private static async Task<int> TrustHostAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
        {
            AnsiConsole.MarkupLine("[red]--host and --user are required[/]");
            return 13;
        }

        var connection = new NodeConnection
        {
            Mode = NodeConnectionMode.Ssh,
            Host = settings.Host,
            Port = settings.Port,
            Username = settings.User
        };

        var fingerprint = await SshNodeExecutor.ProbeHostKeyFingerprintAsync(connection);
        AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(settings.Host)}:{settings.Port}[/] fingerprint: [yellow]{Markup.Escape(fingerprint)}[/]");

        if (!settings.Confirm)
        {
            AnsiConsole.MarkupLine("[grey]not trusted — re-run with --confirm after verifying this fingerprint out-of-band[/]");
            return 0;
        }

        var knownHosts = KnownHostsStore.Default();
        knownHosts.Trust(settings.Host, settings.Port, fingerprint);
        AnsiConsole.MarkupLine("[green]trusted[/] and saved to known_hosts.json");
        return 0;
    }

    private async Task<int> InstallAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Topology))
        {
            AnsiConsole.MarkupLine("[red]--topology is required[/]");
            return 13;
        }

        var topology = await ClusterTopologyJson.LoadAsync(Path.GetFullPath(settings.Topology));
        var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));

        var result = await provisioningService.InstallAsync(new ClusterProvisionOptions
        {
            Topology = topology,
            AllowProduction = settings.AllowProduction
        }, progress);

        foreach (var step in result.CompletedSteps)
        {
            AnsiConsole.MarkupLine($"[green]done[/] {Markup.Escape(step)}");
        }
        if (!result.Ok)
        {
            foreach (var error in result.Errors)
            {
                AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(error)}");
            }
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]cluster '{Markup.Escape(topology.ClusterName)}' installed[/]");
        return 0;
    }

    private async Task<int> UpgradeAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Topology) || string.IsNullOrWhiteSpace(settings.ToVersion))
        {
            AnsiConsole.MarkupLine("[red]--topology and --to-version are required[/]");
            return 13;
        }

        var topology = await ClusterTopologyJson.LoadAsync(Path.GetFullPath(settings.Topology));
        var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));

        var result = await provisioningService.UpgradeAsync(new ClusterProvisionOptions
        {
            Topology = topology,
            AllowProduction = settings.AllowProduction
        }, settings.ToVersion, progress);

        foreach (var node in result.UpgradedNodes)
        {
            AnsiConsole.MarkupLine($"[green]upgraded[/] {Markup.Escape(node)}");
        }
        foreach (var node in result.SkippedNodes)
        {
            AnsiConsole.MarkupLine($"[grey]skipped (already current)[/] {Markup.Escape(node)}");
        }
        foreach (var error in result.Errors)
        {
            AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(error)}");
        }

        return result.Ok ? 0 : 1;
    }

    private async Task<int> StatusAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Topology))
        {
            AnsiConsole.MarkupLine("[red]--topology is required[/]");
            return 13;
        }

        var topology = await ClusterTopologyJson.LoadAsync(Path.GetFullPath(settings.Topology));
        var validation = ClusterTopologyValidator.Validate(topology);
        foreach (var error in validation.Errors)
        {
            AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(error)}");
        }
        foreach (var warning in validation.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]warning[/] {Markup.Escape(warning)}");
        }

        var plan = await upgradePlanner.PlanAsync(topology, topology.Version);
        var table = new Table().AddColumn("node").AddColumn("role").AddColumn("os").AddColumn("current").AddColumn("target").AddColumn("status")
            .Border(TableBorder.Rounded);
        foreach (var step in plan.Steps)
        {
            table.AddRow(
                Markup.Escape(step.Node.Name),
                step.Node.Role.ToString().ToLowerInvariant(),
                Markup.Escape($"{step.Os.DistroId} {step.Os.VersionId} ({step.Os.Arch})"),
                Markup.Escape(step.CurrentVersion ?? "(not installed)"),
                Markup.Escape(step.TargetVersion),
                step.NeedsUpgrade ? "[yellow]upgrade needed[/]" : "[green]up to date[/]");
        }
        AnsiConsole.Write(table);
        return validation.Ok ? 0 : 1;
    }

    private async Task<int> DiscoverAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Output))
        {
            AnsiConsole.MarkupLine("[red]--output is required[/]");
            return 13;
        }
        if (string.IsNullOrWhiteSpace(settings.ClusterName))
        {
            AnsiConsole.MarkupLine("[red]--cluster-name is required[/]");
            return 13;
        }
        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            AnsiConsole.MarkupLine("[red]--version is required[/]");
            return 13;
        }
        if (string.IsNullOrWhiteSpace(settings.User))
        {
            AnsiConsole.MarkupLine("[red]--user is required (SSH username applied to every discovered node)[/]");
            return 13;
        }
        if (!TryParseDistro(settings.Distro, out var distro))
        {
            AnsiConsole.MarkupLine($"[red]unsupported distro:[/] {Markup.Escape(settings.Distro)}");
            return 13;
        }

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

        var fixedAddresses = string.IsNullOrWhiteSpace(settings.FixedRegistrationAddresses)
            ? Array.Empty<string>()
            : settings.FixedRegistrationAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await discoverer.DiscoverAsync(new ClusterDiscoveryOptions
        {
            Context = ctxName,
            ClusterName = settings.ClusterName,
            Distro = distro,
            Version = settings.Version,
            LocalArtifactBundlePath = string.IsNullOrWhiteSpace(settings.BundlePath) ? "REPLACE_WITH_LOCAL_ARTIFACT_BUNDLE_PATH" : settings.BundlePath,
            SshUsername = settings.User,
            SshPrivateKeyVaultId = settings.SshKeyVaultId,
            SshPrivateKeyPath = settings.SshKeyPath,
            SshPort = settings.Port,
            FixedRegistrationAddresses = fixedAddresses
        });

        foreach (var error in result.Errors)
        {
            AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(error)}");
        }
        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]warning[/] {Markup.Escape(warning)}");
        }

        var table = new Table().AddColumn("node").AddColumn("role").AddColumn("host").AddColumn("init").Border(TableBorder.Rounded);
        foreach (var node in result.Topology.Nodes)
        {
            table.AddRow(
                Markup.Escape(node.Name),
                node.Role.ToString().ToLowerInvariant(),
                Markup.Escape(node.Connection.Host ?? "-"),
                node.IsInitServer ? "[green]yes[/]" : "-");
        }
        AnsiConsole.Write(table);

        var outputPath = Path.GetFullPath(settings.Output);
        await ClusterTopologyJson.SaveAsync(outputPath, result.Topology);
        AnsiConsole.MarkupLine($"[green]wrote[/] {Markup.Escape(outputPath)}");
        AnsiConsole.MarkupLine("[grey]this is a best-effort topology — review credentials, localArtifactBundlePath, and any warnings/errors above before using it with install/upgrade[/]");

        return result.Errors.Count == 0 ? 0 : 1;
    }

    private static bool TryParseDistro(string raw, out DistroKind distro)
    {
        switch (raw.ToLowerInvariant())
        {
            case "rke2":
                distro = DistroKind.Rke2;
                return true;
            case "k3s":
                distro = DistroKind.K3s;
                return true;
            case "kubeadm":
            case "kubeadm-native":
                distro = DistroKind.KubeadmNative;
                return true;
            default:
                distro = default;
                return false;
        }
    }
}
