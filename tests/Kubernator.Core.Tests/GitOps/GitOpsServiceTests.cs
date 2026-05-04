using Kubernator.Core.GitOps;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;

namespace Kubernator.Core.Tests.GitOps;

public sealed class GitOpsServiceTests
{
    private readonly StrategySelector strategy = new();
    private readonly GitOpsService service = new();

    [Fact]
    public async Task Generates_application_and_appproject_files()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        var result = await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app"
        });

        File.Exists(Path.Combine(temp.Path, "application.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "appproject.yaml")).Should().BeTrue();
        result.WrittenFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task Application_emits_helm_block_when_source_kind_helm()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app",
            SourceKind = GitOpsSourceKind.Helm,
            SourcePath = "charts/app"
        });

        var app = await File.ReadAllTextAsync(Path.Combine(temp.Path, "application.yaml"));
        app.Should().Contain("kind: Application");
        app.Should().Contain("path: charts/app");
        app.Should().Contain("helm:");
        app.Should().Contain($"releaseName: {plan.ImageName}");
        app.Should().NotContain("directory:");
    }

    [Fact]
    public async Task Application_emits_kustomize_block_when_source_kind_kustomize()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app",
            SourceKind = GitOpsSourceKind.Kustomize,
            SourcePath = "kustomize/overlays/production"
        });

        var app = await File.ReadAllTextAsync(Path.Combine(temp.Path, "application.yaml"));
        app.Should().Contain("kustomize: {}");
        app.Should().NotContain("helm:");
        app.Should().NotContain("directory:");
    }

    [Fact]
    public async Task Application_emits_directory_recurse_block_by_default()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app"
        });

        var app = await File.ReadAllTextAsync(Path.Combine(temp.Path, "application.yaml"));
        app.Should().Contain("directory:");
        app.Should().Contain("recurse: true");
    }

    [Fact]
    public async Task Application_includes_sync_policy_with_retry()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app"
        });

        var app = await File.ReadAllTextAsync(Path.Combine(temp.Path, "application.yaml"));
        app.Should().Contain("syncPolicy:");
        app.Should().Contain("automated:");
        app.Should().Contain("prune: true");
        app.Should().Contain("selfHeal: true");
        app.Should().Contain("- CreateNamespace=true");
        app.Should().Contain("retry:");
        app.Should().Contain("limit: 5");
    }

    [Fact]
    public async Task AppProject_emits_default_readonly_and_admin_roles_when_none_specified()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app"
        });

        var project = await File.ReadAllTextAsync(Path.Combine(temp.Path, "appproject.yaml"));
        project.Should().Contain("kind: AppProject");
        project.Should().Contain("roles:");
        project.Should().Contain("name: readonly");
        project.Should().Contain("name: admin");
        project.Should().Contain($"proj:{plan.ImageName}:readonly");
        project.Should().Contain($"proj:{plan.ImageName}:admin");
    }

    [Fact]
    public async Task AppProject_skips_default_roles_when_explicit_empty_list_provided()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app",
            Roles = [new ProjectRole { Name = "viewer", Policies = ["p, proj:demo:viewer, applications, get, demo/*, allow"] }]
        });

        var project = await File.ReadAllTextAsync(Path.Combine(temp.Path, "appproject.yaml"));
        project.Should().Contain("name: viewer");
        project.Should().NotContain("name: readonly");
        project.Should().NotContain("name: admin");
    }

    [Fact]
    public async Task AppProject_emits_groups_when_supplied()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        var defaults = ProjectRoleDefaults.ReadonlyAndAdmin(plan.ImageName);
        var rolesWithGroups = defaults.Select(r => r.Name == "admin"
            ? r with { Groups = ["team:platform"] }
            : r with { Groups = ["team:devs"] }).ToArray();

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app",
            Roles = rolesWithGroups
        });

        var project = await File.ReadAllTextAsync(Path.Combine(temp.Path, "appproject.yaml"));
        project.Should().Contain("groups:");
        project.Should().Contain("team:platform");
        project.Should().Contain("team:devs");
    }

    [Fact]
    public async Task Throws_when_repo_url_missing()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        var act = async () => await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = ""
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ApplicationName_and_ProjectName_overrides_apply()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet());

        await service.GenerateAsync(plan, new GitOpsOptions
        {
            OutputDirectory = temp.Path,
            RepoUrl = "https://git.example.com/org/app",
            ApplicationName = "Custom App!",
            ProjectName = "team-X"
        });

        var app = await File.ReadAllTextAsync(Path.Combine(temp.Path, "application.yaml"));
        var project = await File.ReadAllTextAsync(Path.Combine(temp.Path, "appproject.yaml"));

        app.Should().Contain("name: custom-app");
        app.Should().Contain("project: team-x");
        project.Should().Contain("name: team-x");
    }
}
