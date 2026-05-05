using System.ComponentModel;
using Kubernator.Core.Packaging.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class KeygenCommand : AsyncCommand<KeygenCommand.Settings>
{
    private readonly ICosignSigner signer;

    public KeygenCommand(ICosignSigner signer)
    {
        this.signer = signer;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-o|--output <dir>")]
        [Description("Directory to write the key pair into.")]
        public string OutputDirectory { get; init; } = ".";

        [CommandOption("--name <name>")]
        [Description("Base file name (default: cosign).")]
        public string BaseName { get; init; } = "cosign";

        [CommandOption("--no-password")]
        [Description("Generate an unencrypted private key (not recommended).")]
        public bool NoPassword { get; init; }

        [CommandOption("--password <pwd>")]
        [Description("Passphrase for the private key (overrides interactive prompt; KUBERNATOR_KEY_PASSWORD env var also accepted).")]
        public string? Password { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        string? passphrase = null;
        if (!settings.NoPassword)
        {
            passphrase = settings.Password
                ?? Environment.GetEnvironmentVariable("KUBERNATOR_KEY_PASSWORD");
            if (passphrase is null)
            {
                if (Console.IsInputRedirected)
                {
                    AnsiConsole.MarkupLine("[red]passphrase required (pass --password, --no-password, or set KUBERNATOR_KEY_PASSWORD)[/]");
                    return 6;
                }
                passphrase = AnsiConsole.Prompt(new TextPrompt<string>("Passphrase for new key:").Secret());
            }
            if (string.IsNullOrEmpty(passphrase))
            {
                AnsiConsole.MarkupLine("[red]passphrase cannot be empty (use --no-password to skip encryption explicitly)[/]");
                return 6;
            }
            var confirm = settings.Password is not null || Environment.GetEnvironmentVariable("KUBERNATOR_KEY_PASSWORD") is not null
                ? passphrase
                : AnsiConsole.Prompt(new TextPrompt<string>("Confirm passphrase:").Secret());
            if (!string.Equals(passphrase, confirm, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine("[red]passphrases do not match[/]");
                return 7;
            }
        }

        var pair = await signer.GenerateKeyPairAsync(
            System.IO.Path.GetFullPath(settings.OutputDirectory),
            settings.BaseName,
            passphrase);

        AnsiConsole.MarkupLine($"[green]private[/] {Markup.Escape(pair.PrivateKeyPath)} ({(pair.PrivateKeyEncrypted ? "encrypted" : "[red]unencrypted[/]")})");
        AnsiConsole.MarkupLine($"[green]public[/]  {Markup.Escape(pair.PublicKeyPath)}");
        return 0;
    }
}
