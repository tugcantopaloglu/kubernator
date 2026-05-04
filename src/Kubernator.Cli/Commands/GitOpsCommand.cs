using System.ComponentModel;
using Kubernator.Core.Abstractions;
using Kubernator.Core.GitOps;
using Kubernator.Core.Strategy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Commands;

internal sealed class GitOpsCommand : AsyncCommand<GitOpsCommand.Settings>
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGitOpsService gitops;

    public GitOpsCommand(IAnalysisService analysis, IStrategySelector strategy, IGitOpsService gitops)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.gitops = gitops;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a published application output (used for default naming).")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--repo-url <url>")]
        [Description("Git repository URL for the Argo CD source.")]
        public string RepoUrl { get; init; } = string.Empty;

        [CommandOption("--target-revision <rev>")]
        public string TargetRevision { get; init; } = "HEAD";

        [CommandOption("--source-path <path>")]
        public string SourcePath { get; init; } = ".";

        [CommandOption("--source-kind <kind>")]
        [Description("directory | helm | kustomize")]
        public string SourceKind { get; init; } = "directory";

        [CommandOption("--namespace <ns>")]
        public string DestinationNamespace { get; init; } = "default";

        [CommandOption("--server <url>")]
        public string DestinationServer { get; init; } = "https://kubernetes.default.svc";

        [CommandOption("--argo-namespace <ns>")]
        public string ArgoNamespace { get; init; } = "argocd";

        [CommandOption("--app-name <name>")]
        public string? AppName { get; init; }

        [CommandOption("--project <name>")]
        public string? ProjectName { get; init; }

        [CommandOption("--no-automated")]
        public bool NoAutomated { get; init; }

        [CommandOption("--no-self-heal")]
        public bool NoSelfHeal { get; init; }

        [CommandOption("--no-prune")]
        public bool NoPrune { get; init; }

        [CommandOption("--no-create-namespace")]
        public bool NoCreateNamespace { get; init; }

        [CommandOption("--allowed-source <repo>")]
        public string[]? AllowedSources { get; init; }

        [CommandOption("--no-default-roles")]
        [Description("Skip emitting the default readonly + admin AppProject roles.")]
        public bool NoDefaultRoles { get; init; }

        [CommandOption("--admin-group <group>")]
        [Description("Subject (oidc group / sso) granted the admin role. Repeatable.")]
        public string[]? AdminGroups { get; init; }

        [CommandOption("--readonly-group <group>")]
        [Description("Subject (oidc group / sso) granted the readonly role. Repeatable.")]
        public string[]? ReadonlyGroups { get; init; }

        [CommandOption("-o|--output <dir>")]
        public string? OutputDirectory { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return ValidationResult.Error("path is required");
            }
            if (string.IsNullOrWhiteSpace(RepoUrl))
            {
                return ValidationResult.Error("--repo-url is required");
            }
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Path);
        var descriptor = await analysis.AnalyzeAsync(path);
        var plan = strategy.Plan(descriptor);

        var sourceKind = settings.SourceKind.ToLowerInvariant() switch
        {
            "helm" => GitOpsSourceKind.Helm,
            "kustomize" => GitOpsSourceKind.Kustomize,
            _ => GitOpsSourceKind.Directory
        };

        var output = settings.OutputDirectory ?? System.IO.Path.Combine(path, ".kubernator", "argocd");
        var projectName = settings.ProjectName ?? plan.ImageName;
        var roles = BuildRoles(projectName, settings);

        var options = new GitOpsOptions
        {
            OutputDirectory = System.IO.Path.GetFullPath(output),
            RepoUrl = settings.RepoUrl,
            TargetRevision = settings.TargetRevision,
            SourcePath = settings.SourcePath,
            SourceKind = sourceKind,
            DestinationServer = settings.DestinationServer,
            DestinationNamespace = settings.DestinationNamespace,
            ArgoNamespace = settings.ArgoNamespace,
            ApplicationName = settings.AppName,
            ProjectName = settings.ProjectName,
            AutomatedSync = !settings.NoAutomated,
            SelfHeal = !settings.NoSelfHeal,
            Prune = !settings.NoPrune,
            CreateNamespace = !settings.NoCreateNamespace,
            AllowedSourceRepos = settings.AllowedSources is { Length: > 0 } ? settings.AllowedSources : ["*"],
            Roles = roles
        };

        var result = await gitops.GenerateAsync(plan, options);

        AnsiConsole.MarkupLine($"[green]output[/]   {Markup.Escape(result.OutputDirectory)}");
        var table = new Table().AddColumn("file").Border(TableBorder.Rounded);
        foreach (var f in result.WrittenFiles)
        {
            table.AddRow(Markup.Escape(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, f)));
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static IReadOnlyList<ProjectRole> BuildRoles(string projectName, Settings settings)
    {
        if (settings.NoDefaultRoles)
        {
            return [];
        }

        var defaults = ProjectRoleDefaults.ReadonlyAndAdmin(projectName);
        var withGroups = new List<ProjectRole>();
        foreach (var role in defaults)
        {
            var groups = role.Name switch
            {
                "admin" => (IReadOnlyList<string>)(settings.AdminGroups ?? []),
                "readonly" => (IReadOnlyList<string>)(settings.ReadonlyGroups ?? []),
                _ => []
            };
            withGroups.Add(role with { Groups = groups });
        }
        return withGroups;
    }
}
