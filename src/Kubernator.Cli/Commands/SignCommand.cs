using System.ComponentModel;
using System.Security.Cryptography;
using Kubernator.Core.Packaging.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class SignCommand : AsyncCommand<SignCommand.Settings>
{
    private readonly ICosignSigner signer;

    public SignCommand(ICosignSigner signer)
    {
        this.signer = signer;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<bundle>")]
        [Description("Path to the .kubpack bundle (or any blob) to sign.")]
        public string Bundle { get; init; } = string.Empty;

        [CommandOption("-k|--key <path>")]
        [Description("Private key in PKCS#8 PEM format.")]
        public string KeyPath { get; init; } = string.Empty;

        [CommandOption("--password <pwd>")]
        [Description("Passphrase for the private key (env: KUBERNATOR_KEY_PASSWORD).")]
        public string? Password { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Bundle)) return ValidationResult.Error("bundle path is required");
            if (string.IsNullOrWhiteSpace(KeyPath)) return ValidationResult.Error("--key is required");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var bundle = System.IO.Path.GetFullPath(settings.Bundle);
        var key = System.IO.Path.GetFullPath(settings.KeyPath);

        var passphrase = settings.Password ?? Environment.GetEnvironmentVariable("KUBERNATOR_KEY_PASSWORD");
        var keyText = await File.ReadAllTextAsync(key);
        if (keyText.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal) && string.IsNullOrEmpty(passphrase))
        {
            passphrase = AnsiConsole.Prompt(new TextPrompt<string>("Passphrase:").Secret());
        }

        try
        {
            var result = await signer.SignBlobAsync(bundle, key, passphrase);
            AnsiConsole.MarkupLine($"[green]signature[/] {Markup.Escape(result.SignaturePath)}");
            AnsiConsole.MarkupLine($"[green]public copy[/] {Markup.Escape(result.PublicKeyCopyPath)}");
            return 0;
        }
        catch (CryptographicException ex)
        {
            AnsiConsole.MarkupLine($"[red]signing failed:[/] {Markup.Escape(ex.Message)}");
            return 8;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]signing failed:[/] {Markup.Escape(ex.Message)}");
            return 9;
        }
    }
}

