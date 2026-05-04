using System.ComponentModel;
using Kubernator.Core.Vulnerabilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class VulnDbCommand : AsyncCommand<VulnDbCommand.Settings>
{
    private readonly IVulnerabilityDatabase database;

    public VulnDbCommand(IVulnerabilityDatabase database)
    {
        this.database = database;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<action>")]
        [Description("Action: status | update | import | import-zip | export")]
        public string Action { get; init; } = string.Empty;

        [CommandOption("--ecosystem <name>")]
        [Description("Ecosystem (NuGet | npm | PyPI | Maven | Go); accepts lower-case aliases.")]
        public string? Ecosystem { get; init; }

        [CommandOption("--zip <path>")]
        [Description("Local OSV all.zip path for `import-zip`.")]
        public string? ZipPath { get; init; }

        [CommandOption("--bundle <path>")]
        [Description("vulndb tar.gz bundle path for `import` / `export`.")]
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
        var action = settings.Action.ToLowerInvariant();
        switch (action)
        {
            case "status":
                return await ShowStatusAsync();
            case "update":
                return await UpdateAsync(settings);
            case "import-zip":
                return await ImportZipAsync(settings);
            case "import":
                return await ImportBundleAsync(settings);
            case "export":
                return await ExportBundleAsync(settings);
            default:
                AnsiConsole.MarkupLine($"[red]unknown action:[/] {Markup.Escape(settings.Action)}");
                AnsiConsole.MarkupLine("[grey]use one of: status, update, import-zip, import, export[/]");
                return 12;
        }
    }

    private async Task<int> ShowStatusAsync()
    {
        AnsiConsole.MarkupLine($"[green]root[/]    {Markup.Escape(database.RootDirectory)}");
        var manifest = await database.GetManifestAsync();
        if (manifest is null)
        {
            AnsiConsole.MarkupLine("[grey]no database imported yet[/]");
            return 0;
        }
        AnsiConsole.MarkupLine($"[green]updated[/] {manifest.UpdatedAt:O}");
        var table = new Table().AddColumn("ecosystem").AddColumn("packages").AddColumn("vulnerabilities").AddColumn("imported")
            .Border(TableBorder.Rounded);
        foreach (var (eco, summary) in manifest.Ecosystems)
        {
            table.AddRow(
                Markup.Escape(eco),
                summary.PackageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                summary.VulnerabilityCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                summary.LastImportedAt?.ToString("O") ?? "(unknown)");
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private async Task<int> UpdateAsync(Settings settings)
    {
        var ecosystems = ResolveEcosystems(settings.Ecosystem);
        foreach (var eco in ecosystems)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating [cyan]{eco}[/] from OSV", async _ =>
                {
                    var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));
                    var result = await database.DownloadOsvAsync(eco, progress);
                    AnsiConsole.MarkupLine($"[green]{eco}[/]: {result.PackageCount} packages, {result.VulnerabilityCount} vulnerabilities");
                });
        }
        return 0;
    }

    private async Task<int> ImportZipAsync(Settings settings)
    {
        if (string.IsNullOrEmpty(settings.ZipPath))
        {
            AnsiConsole.MarkupLine("[red]--zip is required[/]");
            return 13;
        }
        if (string.IsNullOrEmpty(settings.Ecosystem))
        {
            AnsiConsole.MarkupLine("[red]--ecosystem is required[/]");
            return 13;
        }
        var ecosystem = Ecosystems.Normalize(settings.Ecosystem);
        var result = await database.ImportOsvZipAsync(ecosystem, System.IO.Path.GetFullPath(settings.ZipPath));
        AnsiConsole.MarkupLine($"[green]{ecosystem}[/]: {result.PackageCount} packages, {result.VulnerabilityCount} vulnerabilities");
        return 0;
    }

    private async Task<int> ImportBundleAsync(Settings settings)
    {
        if (string.IsNullOrEmpty(settings.BundlePath))
        {
            AnsiConsole.MarkupLine("[red]--bundle is required[/]");
            return 13;
        }
        await database.ImportBundleAsync(System.IO.Path.GetFullPath(settings.BundlePath));
        AnsiConsole.MarkupLine($"[green]vulndb[/] imported from {Markup.Escape(settings.BundlePath)}");
        return 0;
    }

    private async Task<int> ExportBundleAsync(Settings settings)
    {
        var output = settings.BundlePath ?? System.IO.Path.Combine(Environment.CurrentDirectory, "vulndb.tar.gz");
        var path = await database.ExportBundleAsync(System.IO.Path.GetFullPath(output));
        AnsiConsole.MarkupLine($"[green]vulndb[/] exported to {Markup.Escape(path)}");
        return 0;
    }

    private static IEnumerable<string> ResolveEcosystems(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            yield return Ecosystems.NuGet;
            yield return Ecosystems.Npm;
            yield return Ecosystems.PyPI;
            yield return Ecosystems.Maven;
            yield return Ecosystems.Go;
            yield break;
        }
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Ecosystems.Normalize(part);
        }
    }
}
