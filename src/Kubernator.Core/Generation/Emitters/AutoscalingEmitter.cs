namespace Kubernator.Core.Generation.Emitters;

internal static class AutoscalingEmitter
{
    public static string Hpa(string name, string ns, ScalingOptions scaling)
    {
        var min = scaling.HpaMinReplicas ?? 2;
        var max = scaling.HpaMaxReplicas ?? Math.Max(min, 4);
        var w = new IndentedTextWriter();
        w.Line("apiVersion: autoscaling/v2");
        w.Line("kind: HorizontalPodAutoscaler");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Outdent();
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("scaleTargetRef:");
        w.Indent();
        w.Line("apiVersion: apps/v1");
        w.Line("kind: Deployment");
        w.Line($"name: {YamlValue.String(name)}");
        w.Outdent();
        w.Line($"minReplicas: {min}");
        w.Line($"maxReplicas: {max}");
        w.Line("metrics:");
        w.Indent();
        if (scaling.HpaTargetCpuUtilization is { } cpu)
        {
            EmitResourceMetric(w, "cpu", cpu);
        }
        if (scaling.HpaTargetMemoryUtilization is { } memory)
        {
            EmitResourceMetric(w, "memory", memory);
        }
        if (scaling.HpaTargetCpuUtilization is null && scaling.HpaTargetMemoryUtilization is null)
        {
            EmitResourceMetric(w, "cpu", 75);
        }
        w.Outdent();
        w.Line("behavior:");
        w.Indent();
        w.Line("scaleDown:");
        w.Indent();
        w.Line("stabilizationWindowSeconds: 300");
        w.Line("policies:");
        w.Indent();
        w.Line("- type: Percent");
        w.Indent();
        w.Line("value: 50");
        w.Line("periodSeconds: 60");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Line("scaleUp:");
        w.Indent();
        w.Line("stabilizationWindowSeconds: 60");
        w.Line("policies:");
        w.Indent();
        w.Line("- type: Percent");
        w.Indent();
        w.Line("value: 100");
        w.Line("periodSeconds: 30");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string Pdb(string name, string ns, ScalingOptions scaling)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: policy/v1");
        w.Line("kind: PodDisruptionBudget");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Outdent();
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("selector:");
        w.Indent();
        w.Line("matchLabels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Outdent();
        w.Outdent();
        if (scaling.PdbMinAvailable is { } min)
        {
            w.Line($"minAvailable: {min}");
        }
        else if (!string.IsNullOrEmpty(scaling.PdbMinAvailablePercent))
        {
            w.Line($"minAvailable: {YamlValue.String(scaling.PdbMinAvailablePercent)}");
        }
        else if (scaling.PdbMaxUnavailable is { } max)
        {
            w.Line($"maxUnavailable: {max}");
        }
        else if (!string.IsNullOrEmpty(scaling.PdbMaxUnavailablePercent))
        {
            w.Line($"maxUnavailable: {YamlValue.String(scaling.PdbMaxUnavailablePercent)}");
        }
        else
        {
            w.Line($"maxUnavailable: {YamlValue.String("25%")}");
        }
        w.Outdent();
        return w.ToString();
    }

    private static void EmitResourceMetric(IndentedTextWriter w, string resource, int targetUtilization)
    {
        w.Line("- type: Resource");
        w.Indent();
        w.Line("resource:");
        w.Indent();
        w.Line($"name: {resource}");
        w.Line("target:");
        w.Indent();
        w.Line("type: Utilization");
        w.Line($"averageUtilization: {targetUtilization}");
        w.Outdent();
        w.Outdent();
        w.Outdent();
    }
}
