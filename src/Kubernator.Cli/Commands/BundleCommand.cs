using System.ComponentModel;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Packaging;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class BundleCommand : AsyncCommand<BundleCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IBundleService bundleService;
    private readonly IContainerEngineProvider engineProvider;

    public BundleCommand(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IBundleService bundleService,
        IContainerEngineProvider engineProvider)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.bundleService = bundleService;
        this.engineProvider = engineProvider;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output <file>")]
        [Description("Path of the .kubpack bundle to produce.")]
        public string? OutputBundlePath { get; init; }

        [CommandOption("--name <image>")]
        [Description("Container image name.")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        [Description("Container image tag.")]
        public string? ImageTag { get; init; }

        [CommandOption("--namespace <ns>")]
        [Description("Kubernetes namespace.")]
        public string? Namespace { get; init; }

        [CommandOption("--replicas <n>")]
        public int? Replicas { get; init; }

        [CommandOption("--no-sbom")]
        [Description("Skip SBOM generation.")]
        public bool NoSbom { get; init; }

        [CommandOption("--keep-scratch")]
        [Description("Keep the staging directory after bundling (for inspection).")]
        public bool KeepScratch { get; init; }

        [CommandOption("--compression <level>")]
        [Description("gzip level: optimal (default) | fastest | smallest | none.")]
        public string? Compression { get; init; }

        [CommandOption("--source-date-epoch <value>")]
        [Description("Reproducible build epoch (RFC3339 timestamp or Unix seconds). Stamps manifest, SBOM, tar mtimes, and gzip header.")]
        public string? SourceDateEpoch { get; init; }

        [CommandOption("--arch <platform>")]
        [Description("Target platform (linux/amd64, linux/arm64, ...). Repeatable. Multi-platform requires docker buildx.")]
        public string[]? Architectures { get; init; }

        [CommandOption("--hostname <host>")]
        public string? Hostname { get; init; }

        [CommandOption("--extra-host <host>")]
        public string[]? ExtraHosts { get; init; }

        [CommandOption("--tls <mode>")]
        [Description("TLS mode: none | self-signed | cert-manager | user.")]
        public string? Tls { get; init; }

        [CommandOption("--ingress-class <name>")]
        public string? IngressClass { get; init; }

        [CommandOption("--tls-secret <name>")]
        public string? TlsSecretName { get; init; }

        [CommandOption("--cert-issuer <name>")]
        public string? CertIssuer { get; init; }

        [CommandOption("--issuer-kind <kind>")]
        public string? IssuerKind { get; init; }

        [CommandOption("--cert-file <path>")]
        public string? CertFile { get; init; }

        [CommandOption("--key-file <path>")]
        public string? KeyFile { get; init; }

        [CommandOption("--path <path>")]
        public string? IngressPath { get; init; }

        [CommandOption("--no-https-redirect")]
        public bool NoHttpsRedirect { get; init; }

        [CommandOption("--tls-port <port>")]
        public int? TlsPort { get; init; }

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

        [CommandOption("--pdb-min-available-percent <pct>")]
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
            Exposure = exposure,
            Platforms = settings.Architectures
        });

        var requireMulti = (settings.Architectures?.Length ?? 0) > 1;
        IContainerEngine engine;
        try
        {
            engine = await engineProvider.ResolveAsync(requireMulti);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 3;
        }

        var info = await engine.GetInfoAsync();
        AnsiConsole.MarkupLine($"[green]engine[/]   {info.Name} {info.Version} ({info.OperatingSystem}/{info.Architecture})");

        var bundlePath = settings.OutputBundlePath
            ?? System.IO.Path.Combine(Environment.CurrentDirectory, $"{plan.ImageName}-{plan.ImageTag}.kubpack");
        var scratchDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bundlePath) ?? ".", $".{plan.ImageName}-{plan.ImageTag}-scratch");

        var scaling = ScalingBuilder.Build(
            settings.HpaMin,
            settings.HpaMax,
            settings.HpaCpu,
            settings.HpaMemory,
            settings.PdbMinAvailable,
            settings.PdbMaxUnavailable,
            settings.PdbMinAvailablePercent,
            settings.PdbMaxUnavailablePercent);

        var compression = settings.Compression?.Trim().ToLowerInvariant() switch
        {
            null or "" or "optimal" => System.IO.Compression.CompressionLevel.Optimal,
            "fastest" => System.IO.Compression.CompressionLevel.Fastest,
            "smallest" => System.IO.Compression.CompressionLevel.SmallestSize,
            "none" or "no" => System.IO.Compression.CompressionLevel.NoCompression,
            var v => throw new InvalidOperationException($"unknown compression level: {v}")
        };

        DateTimeOffset? sourceDateEpoch = null;
        if (!string.IsNullOrWhiteSpace(settings.SourceDateEpoch))
        {
            var raw = settings.SourceDateEpoch!.Trim();
            if (long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var unix))
            {
                sourceDateEpoch = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            else if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                sourceDateEpoch = parsed;
            }
            else
            {
                throw new InvalidOperationException($"--source-date-epoch must be RFC3339 or Unix seconds, got: {raw}");
            }
        }

        var options = new BundleOptions
        {
            OutputBundlePath = bundlePath,
            ScratchDirectory = scratchDir,
            IncludeSbom = !settings.NoSbom,
            KubernetesNamespace = settings.Namespace,
            Replicas = settings.Replicas ?? 1,
            KeepScratch = settings.KeepScratch,
            Scaling = scaling,
            Compression = compression,
            SourceDateEpoch = sourceDateEpoch
        };

        BundleResult result;
        try
        {
            result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Building bundle (build, save image, sbom, scripts, archive)",
                    async ctx =>
                    {
                        var progress = new Progress<string>(msg => ctx.Status(msg));
                        return await bundleService.CreateAsync(plan, options, engine, progress);
                    });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]bundle failed:[/] {Markup.Escape(ex.Message)}");
            return 4;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]bundle[/]   {Markup.Escape(result.BundlePath)}");
        AnsiConsole.MarkupLine($"[green]size[/]     {result.BundleSizeBytes / 1024 / 1024} MB");
        AnsiConsole.MarkupLine($"[green]sha256[/]   {result.BundleSha256}");
        AnsiConsole.MarkupLine($"[green]images[/]   {result.Manifest.Images.Count}");
        AnsiConsole.MarkupLine($"[green]files[/]    {result.Manifest.Files.Count}");
        AnsiConsole.MarkupLine($"[green]ns[/]       {Markup.Escape(result.Manifest.KubernetesNamespace)}");

        return 0;
    }
}
