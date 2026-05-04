namespace Kubernator.Core.GitOps;

public enum GitOpsSourceKind
{
    Directory,
    Helm,
    Kustomize
}

public sealed record GitOpsOptions
{
    public required string OutputDirectory { get; init; }
    public required string RepoUrl { get; init; }
    public string TargetRevision { get; init; } = "HEAD";
    public string SourcePath { get; init; } = ".";
    public GitOpsSourceKind SourceKind { get; init; } = GitOpsSourceKind.Directory;
    public string DestinationServer { get; init; } = "https://kubernetes.default.svc";
    public string DestinationNamespace { get; init; } = "default";
    public string ArgoNamespace { get; init; } = "argocd";
    public string? ApplicationName { get; init; }
    public string? ProjectName { get; init; }
    public bool AutomatedSync { get; init; } = true;
    public bool SelfHeal { get; init; } = true;
    public bool Prune { get; init; } = true;
    public bool CreateNamespace { get; init; } = true;
    public IReadOnlyList<string> AllowedSourceRepos { get; init; } = ["*"];
    public IReadOnlyList<string> ProjectDestinations { get; init; } = ["*"];
    public IReadOnlyList<ProjectRole> Roles { get; init; } = [];
}

public sealed record ProjectRole
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Policies { get; init; }
    public IReadOnlyList<string> Groups { get; init; } = [];
}

public static class ProjectRoleDefaults
{
    public static IReadOnlyList<ProjectRole> ReadonlyAndAdmin(string projectName) =>
    [
        new ProjectRole
        {
            Name = "readonly",
            Description = "view + sync (read-only)",
            Policies =
            [
                $"p, proj:{projectName}:readonly, applications, get, {projectName}/*, allow",
                $"p, proj:{projectName}:readonly, applications, sync, {projectName}/*, allow"
            ]
        },
        new ProjectRole
        {
            Name = "admin",
            Description = "full control over project applications",
            Policies =
            [
                $"p, proj:{projectName}:admin, applications, *, {projectName}/*, allow",
                $"p, proj:{projectName}:admin, repositories, *, *, allow",
                $"p, proj:{projectName}:admin, clusters, get, *, allow",
                $"p, proj:{projectName}:admin, exec, create, {projectName}/*, allow"
            ]
        }
    ];
}

public sealed record GitOpsResult
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
}
