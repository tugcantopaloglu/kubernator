using System.ComponentModel;
using Kubernator.Core.Vault;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class VaultCommand : AsyncCommand<VaultCommand.Settings>
{
    private readonly IKeyVault vault;

    public VaultCommand(IKeyVault vault)
    {
        this.vault = vault;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<action>")]
        [Description("Action: list | import | remove")]
        public string Action { get; init; } = string.Empty;

        [CommandOption("--name <name>")]
        public string? Name { get; init; }

        [CommandOption("--kind <kind>")]
        [Description("private-key | public-key | certificate")]
        public string? Kind { get; init; }

        [CommandOption("--from <path>")]
        [Description("Source file path for `import`.")]
        public string? From { get; init; }

        [CommandOption("--encrypted")]
        [Description("Mark imported private key as passphrase-encrypted.")]
        public bool Encrypted { get; init; }

        [CommandOption("--id <id>")]
        [Description("Vault entry id for `remove`.")]
        public string? Id { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        switch (settings.Action.ToLowerInvariant())
        {
            case "list": return await ListAsync();
            case "import": return await ImportAsync(settings);
            case "remove": return await RemoveAsync(settings);
            default:
                AnsiConsole.MarkupLine($"[red]unknown action:[/] {Markup.Escape(settings.Action)}");
                AnsiConsole.MarkupLine("[grey]use one of: list, import, remove[/]");
                return 12;
        }
    }

    private async Task<int> ListAsync()
    {
        AnsiConsole.MarkupLine($"[green]root[/] {Markup.Escape(vault.RootDirectory)}");
        var entries = await vault.ListAsync();
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]vault is empty[/]");
            return 0;
        }
        var table = new Table().AddColumn("id").AddColumn("name").AddColumn("kind").AddColumn("enc").AddColumn("fingerprint").AddColumn("created").Border(TableBorder.Rounded);
        foreach (var e in entries)
        {
            table.AddRow(Markup.Escape(e.Id), Markup.Escape(e.Name), e.Kind.ToString().ToLowerInvariant(),
                e.Encrypted ? "yes" : "no",
                Markup.Escape(e.Fingerprint ?? ""),
                e.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private async Task<int> ImportAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.Kind))
        {
            AnsiConsole.MarkupLine("[red]--name, --kind, and --from are required[/]");
            return 13;
        }
        var kind = settings.Kind.ToLowerInvariant() switch
        {
            "private" or "private-key" or "key" => VaultEntryKind.PrivateKey,
            "public" or "public-key" or "pub" => VaultEntryKind.PublicKey,
            "certificate" or "cert" or "crt" => VaultEntryKind.Certificate,
            _ => (VaultEntryKind)(-1)
        };
        if ((int)kind < 0)
        {
            AnsiConsole.MarkupLine($"[red]unknown kind:[/] {Markup.Escape(settings.Kind)}");
            return 13;
        }
        var path = Path.GetFullPath(settings.From);
        var entry = await vault.ImportFromFileAsync(settings.Name, kind, path, settings.Encrypted);
        AnsiConsole.MarkupLine($"[green]imported[/] {entry.Kind.ToString().ToLowerInvariant()} '{Markup.Escape(entry.Name)}' as id [cyan]{entry.Id}[/]");
        return 0;
    }

    private async Task<int> RemoveAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Id))
        {
            AnsiConsole.MarkupLine("[red]--id is required[/]");
            return 13;
        }
        await vault.RemoveAsync(settings.Id);
        AnsiConsole.MarkupLine($"[green]removed[/] {Markup.Escape(settings.Id)}");
        return 0;
    }
}
