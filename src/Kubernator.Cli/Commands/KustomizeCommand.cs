using System.ComponentModel;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Kustomize;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class KustomizeCommand : AsyncCommand<KustomizeCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IKustomizeService kustomize;

    public KustomizeCommand(IAnalysisService analysis, IStrategySelector strategy, IKustomizeService kustomize)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.kustomize = kustomize;
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

        [CommandOption("--namespace <ns>")]
        public string? Namespace { get; init; }

        [CommandOption("--replicas <n>")]
        public int? Replicas { get; init; }

        [CommandOption("--overlay <name>")]
        [Description("Overlay name (repeatable). Default: production, staging, dev.")]
        public string[]? Overlays { get; init; }

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

        [CommandOption("--cert-file <path>")]
        public string? CertFile { get; init; }

        [CommandOption("--key-file <path>")]
        public string? KeyFile { get; init; }

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
                settings.CertFile,
                settings.KeyFile,
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

        var output = settings.OutputDirectory ?? System.IO.Path.Combine(path, ".kubernator", "kustomize");
        var overlays = settings.Overlays is { Length: > 0 } ? settings.Overlays : ["production", "staging", "dev"];
        var options = new KustomizeOptions
        {
            OutputDirectory = output,
            BaseNamespace = settings.Namespace,
            Overlays = overlays,
            Scaling = scaling,
            Exposure = exposure,
            Replicas = settings.Replicas ?? 1
        };

        var result = await kustomize.GenerateAsync(plan, options);

        AnsiConsole.MarkupLine($"[green]base[/]      {Markup.Escape(result.BaseDirectory)}");
        AnsiConsole.MarkupLine($"[green]overlays[/]  {string.Join(", ", overlays)}");
        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var f in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, f)));
        }
        AnsiConsole.Write(table);
        return 0;
    }
}
