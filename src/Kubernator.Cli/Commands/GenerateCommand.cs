using System.ComponentModel;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;

    public GenerateCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <dir>")]
        [Description("Output directory for generated files.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--name <image>")]
        [Description("Container image name (default: derived from assembly).")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        [Description("Container image tag (default: runtime version).")]
        public string? ImageTag { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Kubernetes namespace (default: default).")]
        public string? Namespace { get; init; }

        [CommandOption("--replicas <n>")]
        [Description("Replica count for the Deployment (default: 1).")]
        public int? Replicas { get; init; }

        [CommandOption("--no-overwrite")]
        [Description("Do not overwrite existing generated files.")]
        public bool NoOverwrite { get; init; }

        [CommandOption("--hostname <host>")]
        [Description("Primary public hostname (enables Ingress generation).")]
        public string? Hostname { get; init; }

        [CommandOption("--extra-host <host>")]
        [Description("Additional hostname (repeatable).")]
        public string[]? ExtraHosts { get; init; }

        [CommandOption("--tls <mode>")]
        [Description("TLS mode: none | self-signed | cert-manager | user (default: self-signed when --hostname is set).")]
        public string? Tls { get; init; }

        [CommandOption("--ingress-class <name>")]
        public string? IngressClass { get; init; }

        [CommandOption("--tls-secret <name>")]
        public string? TlsSecretName { get; init; }

        [CommandOption("--cert-issuer <name>")]
        [Description("cert-manager Issuer/ClusterIssuer name.")]
        public string? CertIssuer { get; init; }

        [CommandOption("--issuer-kind <kind>")]
        [Description("ClusterIssuer (default) or Issuer.")]
        public string? IssuerKind { get; init; }

        [CommandOption("--cert-file <path>")]
        [Description("PEM certificate file (required with --tls user).")]
        public string? CertFile { get; init; }

        [CommandOption("--key-file <path>")]
        [Description("PEM private key file (required with --tls user).")]
        public string? KeyFile { get; init; }

        [CommandOption("--path <path>")]
        public string? IngressPath { get; init; }

        [CommandOption("--no-https-redirect")]
        public bool NoHttpsRedirect { get; init; }

        [CommandOption("--tls-port <port>")]
        [Description("Override the backend service port that the Ingress points at.")]
        public int? TlsPort { get; init; }

        [CommandOption("--hpa-min <n>")]
        public int? HpaMin { get; init; }

        [CommandOption("--hpa-max <n>")]
        public int? HpaMax { get; init; }

        [CommandOption("--hpa-cpu <pct>")]
        [Description("HPA target average CPU utilization (percentage).")]
        public int? HpaCpu { get; init; }

        [CommandOption("--hpa-memory <pct>")]
        [Description("HPA target average memory utilization (percentage).")]
        public int? HpaMemory { get; init; }

        [CommandOption("--pdb-min-available <n>")]
        public int? PdbMinAvailable { get; init; }

        [CommandOption("--pdb-max-unavailable <n>")]
        public int? PdbMaxUnavailable { get; init; }

        [CommandOption("--pdb-min-available-percent <pct>")]
        [Description("E.g. 50% — exclusive with --pdb-min-available.")]
        public string? PdbMinAvailablePercent { get; init; }

        [CommandOption("--pdb-max-unavailable-percent <pct>")]
        public string? PdbMaxUnavailablePercent { get; init; }

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

        ExposureOptions? exposure;
        try
        {
            exposure = ExposureBuilder.Build(
                settings.Hostname,
                settings.ExtraHosts,
                settings.Tls,
                settings.IngressClass,
                settings.TlsSecretName,
                settings.CertIssuer,
                settings.IssuerKind,
                settings.CertFile,
                settings.KeyFile,
                settings.IngressPath,
                settings.NoHttpsRedirect,
                settings.TlsPort);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 11;
        }

        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = settings.ImageName,
            ImageTag = settings.ImageTag,
            Exposure = exposure
        });

        var scaling = ScalingBuilder.Build(
            settings.HpaMin,
            settings.HpaMax,
            settings.HpaCpu,
            settings.HpaMemory,
            settings.PdbMinAvailable,
            settings.PdbMaxUnavailable,
            settings.PdbMinAvailablePercent,
            settings.PdbMaxUnavailablePercent);

        var output = settings.OutputDirectory ?? System.IO.Path.Combine(path, ".kubernator");
        var options = new GenerationOptions
        {
            OutputDirectory = output,
            Namespace = settings.Namespace,
            Replicas = settings.Replicas ?? 1,
            OverwriteExisting = !settings.NoOverwrite,
            Scaling = scaling
        };

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Dockerfile and Kubernetes manifests", async _ => await generation.GenerateAsync(plan, options));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]base image[/]   {Markup.Escape(plan.RuntimeImage.DisplayName)} ([grey]{Markup.Escape(plan.RuntimeImage.Reference)}[/])");
        AnsiConsole.MarkupLine($"[green]image[/]        {Markup.Escape(plan.FullImageReference)}");
        AnsiConsole.MarkupLine($"[green]workdir[/]      {Markup.Escape(plan.WorkingDirectory)}");
        AnsiConsole.MarkupLine($"[green]user[/]         {plan.Security.RunAsUser}:{plan.Security.RunAsGroup}");
        AnsiConsole.WriteLine();

        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var file in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, file)));
        }
        AnsiConsole.Write(table);

        return 0;
    }
}
