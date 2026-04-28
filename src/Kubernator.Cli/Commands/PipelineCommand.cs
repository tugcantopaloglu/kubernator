using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.Pipelines;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class PipelineCommand : AsyncCommand<PipelineCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IPipelineService pipelines;

    public PipelineCommand(IAnalysisService analysis, IPipelineService pipelines)
    {
        this.analysis = analysis;
        this.pipelines = pipelines;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output (used to detect language).")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--target <name>")]
        [Description("Pipeline target: gh-actions | gitlab | azure | tekton (default: gh-actions).")]
        public string TargetName { get; init; } = "gh-actions";

        [CommandOption("-o|--output <dir>")]
        [Description("Output directory (default: current directory).")]
        public string OutputDirectory { get; init; } = ".";

        [CommandOption("--name <image>")]
        public string? ImageName { get; init; }

        [CommandOption("--tag <tag>")]
        public string? ImageTag { get; init; }

        [CommandOption("--namespace <ns>")]
        public string Namespace { get; init; } = "default";

        [CommandOption("--registry <url>")]
        public string Registry { get; init; } = "registry.example.com";

        [CommandOption("--publish <path>")]
        [Description("Path inside the repository where the published artefacts live (default: ./publish).")]
        public string PublishPath { get; init; } = "./publish";

        [CommandOption("--sign")]
        [Description("Include a signing step (expects COSIGN_KEY_PATH and COSIGN_PASSWORD secrets).")]
        public bool Sign { get; init; }

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
        if (!TryParseTarget(settings.TargetName, out var target))
        {
            AnsiConsole.MarkupLine($"[red]unknown pipeline target:[/] {Markup.Escape(settings.TargetName)}");
            AnsiConsole.MarkupLine("[grey]use one of: gh-actions, gitlab, azure, tekton[/]");
            return 10;
        }

        var path = System.IO.Path.GetFullPath(settings.Path);
        var descriptor = await analysis.AnalyzeAsync(path);

        var imageName = settings.ImageName ?? descriptor.EntryPoint?.AssemblyName ?? "app";
        var imageTag = settings.ImageTag ?? descriptor.Runtime.Version ?? "0.1.0";

        var options = new PipelineOptions
        {
            AppKind = descriptor.Kind,
            ImageName = SanitizeName(imageName),
            ImageTag = SanitizeTag(imageTag),
            Registry = settings.Registry,
            Namespace = settings.Namespace,
            PublishPath = settings.PublishPath,
            SignBundle = settings.Sign,
            RunVerify = true
        };

        var output = System.IO.Path.GetFullPath(settings.OutputDirectory);
        var result = await pipelines.GenerateAsync(target, options, output);

        AnsiConsole.MarkupLine($"[green]target[/]   {target}");
        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var f in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, f)));
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static bool TryParseTarget(string name, out PipelineTarget target)
    {
        switch (name.ToLowerInvariant())
        {
            case "gh-actions":
            case "github":
            case "github-actions":
                target = PipelineTarget.GitHubActions;
                return true;
            case "gitlab":
            case "gitlab-ci":
                target = PipelineTarget.GitLabCi;
                return true;
            case "azure":
            case "ado":
            case "azure-devops":
                target = PipelineTarget.AzureDevOps;
                return true;
            case "tekton":
                target = PipelineTarget.Tekton;
                return true;
            default:
                target = default;
                return false;
        }
    }

    private static string SanitizeName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string SanitizeTag(string raw)
    {
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '-').ToArray();
        return new string(chars).TrimStart('.', '-').Trim();
    }
}
