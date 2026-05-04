using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Kustomize;

internal static class KustomizeEmitter
{
    public static string BaseKustomization(string name, string ns, BuildPlan plan, IReadOnlyList<string> resources)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: kustomize.config.k8s.io/v1beta1");
        w.Line("kind: Kustomization");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Line($"namePrefix: \"\"");
        w.Line("commonLabels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Outdent();
        w.Line("resources:");
        w.Indent();
        foreach (var r in resources)
        {
            w.Line($"- {YamlValue.String(r)}");
        }
        w.Outdent();
        w.Line("images:");
        w.Indent();
        w.Line($"- name: {YamlValue.String(plan.ImageName)}");
        w.Indent();
        w.Line($"newTag: {YamlValue.String(plan.ImageTag)}");
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string OverlayKustomization(string overlayName, string baseName, BuildPlan plan, KustomizeOptions options)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: kustomize.config.k8s.io/v1beta1");
        w.Line("kind: Kustomization");
        var ns = $"{baseName}-{overlayName}";
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Line("resources:");
        w.Indent();
        w.Line("- ../../base");
        w.Outdent();
        w.Line("commonAnnotations:");
        w.Indent();
        w.Line($"kubernator.dev/overlay: {YamlValue.String(overlayName)}");
        w.Outdent();
        w.Line("patches:");
        w.Indent();
        w.Line("- target:");
        w.Indent();
        w.Indent();
        w.Line("kind: Deployment");
        w.Line($"name: {YamlValue.String(baseName)}");
        w.Outdent();
        w.Line("patch: |-");
        w.Indent();
        w.Line("- op: replace");
        w.Line("  path: /spec/replicas");
        w.Line($"  value: {ResolveOverlayReplicas(overlayName, options.Replicas, options.Scaling)}");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Line("images:");
        w.Indent();
        w.Line($"- name: {YamlValue.String(plan.ImageName)}");
        w.Indent();
        var overlayTag = ResolveOverlayTag(overlayName, plan.ImageTag);
        w.Line($"newTag: {YamlValue.String(overlayTag)}");
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static int ResolveOverlayReplicas(string overlayName, int defaultReplicas, Generation.ScalingOptions? scaling)
    {
        return overlayName.ToLowerInvariant() switch
        {
            "production" or "prod" => Math.Max(scaling?.HpaMinReplicas ?? defaultReplicas, 3),
            "staging" => Math.Max(defaultReplicas, 2),
            "dev" or "development" => 1,
            _ => defaultReplicas
        };
    }

    private static string ResolveOverlayTag(string overlayName, string baseTag)
    {
        return overlayName.ToLowerInvariant() switch
        {
            "dev" or "development" => $"{baseTag}-dev",
            _ => baseTag
        };
    }
}
