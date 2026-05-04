using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Vulnerabilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class ScanCommand : AsyncCommand<ScanCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IVulnerabilityScanner scanner;

    public ScanCommand(IAnalysisService analysis, IVulnerabilityScanner scanner)
    {
        this.analysis = analysis;
        this.scanner = scanner;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--ecosystem <name>")]
        [Description("Override the auto-detected ecosystem.")]
        public string? Ecosystem { get; init; }

        [CommandOption("--min-severity <level>")]
        [Description("low | medium | high | critical (default: low).")]
        public string MinSeverity { get; init; } = "low";

        [CommandOption("--ignore <id>")]
        [Description("Ignore vulnerability id (repeatable).")]
        public string[]? Ignore { get; init; }

        [CommandOption("--json")]
        public bool Json { get; init; }

        [CommandOption("--fail-on <level>")]
        [Description("Exit with non-zero status if any finding meets or exceeds this severity.")]
        public string? FailOn { get; init; }

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

        if (!TryParseSeverity(settings.MinSeverity, out var minSeverity))
        {
            AnsiConsole.MarkupLine($"[red]unknown severity:[/] {Markup.Escape(settings.MinSeverity)}");
            return 14;
        }

        var ignoreSet = settings.Ignore is { Length: > 0 }
            ? new HashSet<string>(settings.Ignore, StringComparer.OrdinalIgnoreCase)
            : null;

        var ecosystem = settings.Ecosystem is null ? null : Ecosystems.Normalize(settings.Ecosystem);
        var scanOptions = new ScanOptions
        {
            EcosystemOverride = ecosystem,
            MinSeverity = minSeverity,
            IgnoreIds = ignoreSet
        };

        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Scanning dependencies", async _ => await scanner.ScanAsync(descriptor, scanOptions));

        if (!result.DatabasePresent)
        {
            AnsiConsole.MarkupLine("[yellow]vulnerability database not found; run `kubernator vulndb update` or `vulndb import`[/]");
        }

        if (settings.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            RenderTable(result);
        }

        if (settings.FailOn is not null && TryParseSeverity(settings.FailOn, out var failOn))
        {
            if (result.Findings.Any(f => f.Severity >= failOn && f.Severity != Severity.Unknown))
            {
                return 15;
            }
        }
        return 0;
    }

    private static void RenderTable(ScanResult result)
    {
        AnsiConsole.MarkupLine($"[green]ecosystem[/]   {Markup.Escape(result.Ecosystem)}");
        AnsiConsole.MarkupLine($"[green]packages[/]    {result.PackagesScanned}");
        AnsiConsole.MarkupLine($"[green]findings[/]    {result.Findings.Count}");
        if (result.DatabaseUpdatedAt is { } updated)
        {
            AnsiConsole.MarkupLine($"[green]db updated[/]  {updated:O}");
        }
        if (result.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]no known vulnerabilities[/]");
            return;
        }

        var table = new Table()
            .AddColumn("severity")
            .AddColumn("id")
            .AddColumn("package")
            .AddColumn("version")
            .AddColumn("fixed in")
            .AddColumn("summary")
            .Border(TableBorder.Rounded);

        foreach (var f in result.Findings)
        {
            var color = f.Severity switch
            {
                Severity.Critical => "red",
                Severity.High => "red",
                Severity.Medium => "yellow",
                Severity.Low => "grey",
                _ => "grey"
            };
            table.AddRow(
                $"[{color}]{f.Severity}[/]",
                Markup.Escape(f.VulnerabilityId),
                Markup.Escape(f.PackageName),
                Markup.Escape(f.PackageVersion),
                Markup.Escape(f.FixedIn ?? "-"),
                Markup.Escape(Truncate(f.Summary ?? string.Empty, 60)));
        }
        AnsiConsole.Write(table);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static bool TryParseSeverity(string raw, out Severity severity)
    {
        switch (raw.ToLowerInvariant())
        {
            case "low":
                severity = Severity.Low;
                return true;
            case "medium":
            case "moderate":
                severity = Severity.Medium;
                return true;
            case "high":
                severity = Severity.High;
                return true;
            case "critical":
                severity = Severity.Critical;
                return true;
            default:
                severity = Severity.Unknown;
                return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
