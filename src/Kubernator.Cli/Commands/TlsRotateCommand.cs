using System.ComponentModel;
using Kubernator.Core.Tls.Rotation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class TlsRotateCommand : AsyncCommand<TlsRotateCommand.Settings>
{
    private readonly ITlsRotationService rotation;

    public TlsRotateCommand(ITlsRotationService rotation)
    {
        this.rotation = rotation;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<secret>")]
        [Description("Name of the kubernetes.io/tls Secret to rotate.")]
        public string SecretName { get; init; } = string.Empty;

        [CommandOption("--hostname <host>")]
        [Description("Primary hostname (CN + first SAN).")]
        public string Hostname { get; init; } = string.Empty;

        [CommandOption("--extra-host <host>")]
        public string[]? AdditionalHostnames { get; init; }

        [CommandOption("--namespace <ns>")]
        public string Namespace { get; init; } = "default";

        [CommandOption("--schedule <cron>")]
        [Description("Cron schedule (default: 0 3 1 * * - 03:00 UTC on the 1st of every month).")]
        public string Schedule { get; init; } = "0 3 1 * *";

        [CommandOption("--days-valid <n>")]
        public int DaysValid { get; init; } = 90;

        [CommandOption("--image <ref>")]
        [Description("Image used by the CronJob (must include openssl + curl). Default: chainguard wolfi-base.")]
        public string Image { get; init; } = "cgr.dev/chainguard/wolfi-base:latest";

        [CommandOption("--service-account <name>")]
        public string? ServiceAccountName { get; init; }

        [CommandOption("--cronjob-name <name>")]
        public string? CronJobName { get; init; }

        [CommandOption("-o|--output <dir>")]
        public string? OutputDirectory { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return ValidationResult.Error("secret name is required");
            }
            if (string.IsNullOrWhiteSpace(Hostname))
            {
                return ValidationResult.Error("--hostname is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = settings.OutputDirectory ?? System.IO.Path.Combine(
            Environment.CurrentDirectory, ".kubernator", "tls-rotation", settings.SecretName);

        var options = new TlsRotationOptions
        {
            OutputDirectory = System.IO.Path.GetFullPath(output),
            SecretName = settings.SecretName,
            Hostname = settings.Hostname,
            AdditionalHostnames = settings.AdditionalHostnames ?? [],
            Namespace = settings.Namespace,
            Schedule = settings.Schedule,
            DaysValid = settings.DaysValid,
            Image = settings.Image,
            ServiceAccountName = settings.ServiceAccountName,
            CronJobName = settings.CronJobName
        };

        var result = await rotation.GenerateAsync(options);

        AnsiConsole.MarkupLine($"[green]output[/]    {Markup.Escape(result.OutputDirectory)}");
        AnsiConsole.MarkupLine($"[grey]sa[/]        {Markup.Escape(result.ResolvedServiceAccountName)}");
        AnsiConsole.MarkupLine($"[grey]cronjob[/]   {Markup.Escape(result.ResolvedCronJobName)}");
        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var f in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, f)));
        }
        AnsiConsole.Write(table);
        return 0;
    }
}
