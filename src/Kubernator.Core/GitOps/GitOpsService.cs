using Kubernator.Core.Strategy;

namespace Kubernator.Core.GitOps;

public sealed class GitOpsService : IGitOpsService
{
    public async Task<GitOpsResult> GenerateAsync(BuildPlan plan, GitOpsOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.RepoUrl))
        {
            throw new InvalidOperationException("repo URL is required for Argo CD Application");
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var appName = SanitizeName(options.ApplicationName ?? plan.ImageName);
        var projectName = SanitizeName(options.ProjectName ?? appName);

        var resolvedOptions = options.Roles.Count == 0
            ? options with { Roles = ProjectRoleDefaults.ReadonlyAndAdmin(projectName) }
            : options;

        var application = ArgoCdEmitter.ApplicationYaml(plan, resolvedOptions, appName, projectName);
        var project = ArgoCdEmitter.AppProjectYaml(plan, resolvedOptions, projectName);

        var written = new List<string>();

        var appPath = Path.Combine(options.OutputDirectory, "application.yaml");
        var projectPath = Path.Combine(options.OutputDirectory, "appproject.yaml");
        await File.WriteAllTextAsync(appPath, application, ct);
        await File.WriteAllTextAsync(projectPath, project, ct);
        written.Add(appPath);
        written.Add(projectPath);

        return new GitOpsResult
        {
            OutputDirectory = options.OutputDirectory,
            WrittenFiles = written
        };
    }

    private static string SanitizeName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name = new string(chars).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }
        return string.IsNullOrEmpty(name) ? "app" : name;
    }
}
