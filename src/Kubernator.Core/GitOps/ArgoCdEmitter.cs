using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.GitOps;

internal static class ArgoCdEmitter
{
    public static string ApplicationYaml(BuildPlan plan, GitOpsOptions options, string appName, string projectName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: argoproj.io/v1alpha1");
        w.Line("kind: Application");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(appName)}");
        w.Line($"namespace: {YamlValue.String(options.ArgoNamespace)}");
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(appName)}");
        w.Line($"app.kubernetes.io/managed-by: {YamlValue.String("kubernator")}");
        w.Outdent();
        w.Line("finalizers:");
        w.Line("  - resources-finalizer.argocd.argoproj.io");
        w.Outdent();

        w.Line("spec:");
        w.Indent();
        w.Line($"project: {YamlValue.String(projectName)}");

        w.Line("source:");
        w.Indent();
        w.Line($"repoURL: {YamlValue.String(options.RepoUrl)}");
        w.Line($"targetRevision: {YamlValue.String(options.TargetRevision)}");
        w.Line($"path: {YamlValue.String(options.SourcePath)}");
        switch (options.SourceKind)
        {
            case GitOpsSourceKind.Helm:
                w.Line("helm:");
                w.Indent();
                w.Line($"releaseName: {YamlValue.String(appName)}");
                w.Outdent();
                break;
            case GitOpsSourceKind.Kustomize:
                w.Line("kustomize: {}");
                break;
            case GitOpsSourceKind.Directory:
            default:
                w.Line("directory:");
                w.Indent();
                w.Line("recurse: true");
                w.Outdent();
                break;
        }
        w.Outdent();

        w.Line("destination:");
        w.Indent();
        w.Line($"server: {YamlValue.String(options.DestinationServer)}");
        w.Line($"namespace: {YamlValue.String(options.DestinationNamespace)}");
        w.Outdent();

        w.Line("syncPolicy:");
        w.Indent();
        if (options.AutomatedSync)
        {
            w.Line("automated:");
            w.Indent();
            w.Line($"prune: {YamlValue.Bool(options.Prune)}");
            w.Line($"selfHeal: {YamlValue.Bool(options.SelfHeal)}");
            w.Outdent();
        }
        w.Line("syncOptions:");
        if (options.CreateNamespace)
        {
            w.Line("  - CreateNamespace=true");
        }
        w.Line("  - PrunePropagationPolicy=foreground");
        w.Line("  - ApplyOutOfSyncOnly=true");
        w.Line("retry:");
        w.Indent();
        w.Line("limit: 5");
        w.Line("backoff:");
        w.Indent();
        w.Line("duration: 5s");
        w.Line("factor: 2");
        w.Line("maxDuration: 3m");
        w.Outdent();
        w.Outdent();
        w.Outdent();

        w.Outdent();
        return w.ToString();
    }

    public static string AppProjectYaml(BuildPlan plan, GitOpsOptions options, string projectName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: argoproj.io/v1alpha1");
        w.Line("kind: AppProject");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(projectName)}");
        w.Line($"namespace: {YamlValue.String(options.ArgoNamespace)}");
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/managed-by: {YamlValue.String("kubernator")}");
        w.Outdent();
        w.Outdent();

        w.Line("spec:");
        w.Indent();
        w.Line($"description: {YamlValue.String($"Project for {plan.ImageName}")}");

        w.Line("sourceRepos:");
        foreach (var repo in options.AllowedSourceRepos)
        {
            w.Line($"  - {YamlValue.String(repo)}");
        }

        w.Line("destinations:");
        foreach (var ns in options.ProjectDestinations)
        {
            w.Line("  - server: " + YamlValue.String(options.DestinationServer));
            w.Line("    namespace: " + YamlValue.String(ns));
        }

        w.Line("clusterResourceWhitelist:");
        w.Line("  - group: \"\"");
        w.Line("    kind: Namespace");
        w.Line("  - group: \"rbac.authorization.k8s.io\"");
        w.Line("    kind: ClusterRole");
        w.Line("  - group: \"rbac.authorization.k8s.io\"");
        w.Line("    kind: ClusterRoleBinding");

        w.Line("namespaceResourceWhitelist:");
        w.Line("  - group: \"\"");
        w.Line("    kind: \"*\"");
        w.Line("  - group: \"apps\"");
        w.Line("    kind: \"*\"");
        w.Line("  - group: \"networking.k8s.io\"");
        w.Line("    kind: \"*\"");
        w.Line("  - group: \"autoscaling\"");
        w.Line("    kind: \"*\"");
        w.Line("  - group: \"policy\"");
        w.Line("    kind: \"*\"");
        w.Line("  - group: \"cert-manager.io\"");
        w.Line("    kind: \"*\"");

        if (options.Roles.Count > 0)
        {
            w.Line("roles:");
            foreach (var role in options.Roles)
            {
                w.Line($"  - name: {YamlValue.String(role.Name)}");
                if (!string.IsNullOrEmpty(role.Description))
                {
                    w.Line($"    description: {YamlValue.String(role.Description!)}");
                }
                if (role.Policies.Count > 0)
                {
                    w.Line("    policies:");
                    foreach (var policy in role.Policies)
                    {
                        w.Line($"      - {YamlValue.String(policy)}");
                    }
                }
                if (role.Groups.Count > 0)
                {
                    w.Line("    groups:");
                    foreach (var group in role.Groups)
                    {
                        w.Line($"      - {YamlValue.String(group)}");
                    }
                }
            }
        }

        w.Line("orphanedResources:");
        w.Indent();
        w.Line("warn: true");
        w.Outdent();
        w.Outdent();

        return w.ToString();
    }
}
