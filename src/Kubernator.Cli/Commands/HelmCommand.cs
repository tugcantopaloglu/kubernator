using System.ComponentModel;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Helm;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class HelmCommand : AsyncCommand<HelmCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IHelmService helm;

    public HelmCommand(IAnalysisService analysis, IStrategySelector strategy, IHelmService helm)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.helm = helm;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <dir>")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--name <image>")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        public string? ImageTag { get; init; }

        [CommandOption("--chart-name <name>")]
        public string? ChartName { get; init; }

        [CommandOption("--chart-version <ver>")]
        public string ChartVersion { get; init; } = "0.1.0";

        [CommandOption("--description <text>")]
        public string? Description { get; init; }

        [CommandOption("--package")]
        [Description("Also produce a packaged .tgz alongside the chart directory.")]
        public bool Package { get; init; }

        [CommandOption("--hostname <host>")]
        public string? Hostname { get; init; }

        [CommandOption("--extra-host <host>")]
        public string[]? ExtraHosts { get; init; }

        [CommandOption("--tls <mode>")]
        public string? Tls { get; init; }

        [CommandOption("--ingress-class <name>")]
        public string? IngressClass { get; init; }

        [CommandOption("--cert-issuer <name>")]
        public string? CertIssuer { get; init; }

        [CommandOption("--issuer-kind <kind>")]
        public string? IssuerKind { get; init; }

        [CommandOption("--hpa-min <n>")]
        public int? HpaMin { get; init; }

        [CommandOption("--hpa-max <n>")]
        public int? HpaMax { get; init; }

        [CommandOption("--hpa-cpu <pct>")]
        public int? HpaCpu { get; init; }

        [CommandOption("--hpa-memory <pct>")]
        public int? HpaMemory { get; init; }

        [CommandOption("--pdb-min-available <n>")]
        public int? PdbMinAvailable { get; init; }

        [CommandOption("--pdb-max-unavailable <n>")]
        public int? PdbMaxUnavailable { get; init; }

        [CommandOption("--replicas <n>")]
        public int? Replicas { get; init; }

        [CommandOption("--namespace <ns>")]
        public string? Namespace { get; init; }

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
        var descriptor = await analysis.AnalyzeAsync(path);

        ExposureOptions? exposure;
        try
        {
            exposure = ExposureBuilder.Build(
                settings.Hostname,
                settings.ExtraHosts,
                settings.Tls,
                settings.IngressClass,
                tlsSecretName: null,
                settings.CertIssuer,
                settings.IssuerKind,
                certFile: null,
                keyFile: null,
                path: null,
                noHttpsRedirect: false,
                overridePort: null);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 11;
        }

        var scaling = ScalingBuilder.Build(
            settings.HpaMin,
            settings.HpaMax,
            settings.HpaCpu,
            settings.HpaMemory,
            settings.PdbMinAvailable,
            settings.PdbMaxUnavailable,
            null,
            null);

        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = settings.ImageName,
            ImageTag = settings.ImageTag,
            Exposure = exposure
        });

        var output = settings.OutputDirectory ?? System.IO.Path.Combine(path, ".kubernator", "helm");
        var options = new HelmOptions
        {
            OutputDirectory = output,
            ChartName = settings.ChartName,
            ChartVersion = settings.ChartVersion,
            Description = settings.Description,
            Package = settings.Package,
            Scaling = scaling,
            Exposure = exposure,
            Namespace = settings.Namespace,
            Replicas = settings.Replicas ?? 1
        };

        var result = await helm.GenerateAsync(plan, options);
        AnsiConsole.MarkupLine($"[green]chart[/]    {Markup.Escape(result.ChartDirectory)}");
        AnsiConsole.MarkupLine($"[green]files[/]    {result.WrittenFiles.Count}");
        if (result.PackageFile is not null)
        {
            AnsiConsole.MarkupLine($"[green]package[/]  {Markup.Escape(result.PackageFile)}");
        }
        return 0;
    }
}
