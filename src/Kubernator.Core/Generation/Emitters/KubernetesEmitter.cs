using Kubernator.Core.Strategy;

namespace Kubernator.Core.Generation.Emitters;

internal static class KubernetesEmitter
{
    public static string Deployment(BuildPlan plan, GenerationOptions options, string name, string ns)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: apps/v1");
        w.Line("kind: Deployment");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        EmitLabels(w, name);
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line($"replicas: {options.Replicas}");
        w.Line("revisionHistoryLimit: 3");
        w.Line("selector:");
        w.Indent();
        w.Line("matchLabels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Outdent();
        w.Outdent();
        w.Line("strategy:");
        w.Indent();
        w.Line("type: RollingUpdate");
        w.Line("rollingUpdate:");
        w.Indent();
        w.Line("maxSurge: 1");
        w.Line("maxUnavailable: 0");
        w.Outdent();
        w.Outdent();
        w.Line("template:");
        w.Indent();
        w.Line("metadata:");
        w.Indent();
        EmitLabels(w, name);
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("automountServiceAccountToken: false");
        EmitPodSecurityContext(w, plan.Security);
        w.Line("containers:");
        w.Indent();
        EmitContainer(w, plan, options, name);
        w.Outdent();
        if (plan.Security.WritableMounts.Count > 0)
        {
            w.Line("volumes:");
            w.Indent();
            for (int i = 0; i < plan.Security.WritableMounts.Count; i++)
            {
                w.Line($"- name: writable-{i}");
                w.Indent();
                w.Line("emptyDir:");
                w.Indent();
                w.Line("medium: Memory");
                w.Line("sizeLimit: 64Mi");
                w.Outdent();
                w.Outdent();
            }
            w.Outdent();
        }
        w.Outdent();
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string Service(BuildPlan plan, string name, string ns)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: v1");
        w.Line("kind: Service");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name)}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        EmitLabels(w, name);
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("type: ClusterIP");
        w.Line("selector:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Outdent();
        w.Line("ports:");
        w.Indent();
        foreach (var port in plan.ExposedPorts)
        {
            w.Line($"- name: port-{port}");
            w.Indent();
            w.Line($"port: {port}");
            w.Line($"targetPort: {port}");
            w.Line("protocol: TCP");
            w.Outdent();
        }
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string NetworkPolicy(BuildPlan plan, string name, string ns)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: networking.k8s.io/v1");
        w.Line("kind: NetworkPolicy");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(name + "-default-deny")}");
        w.Line($"namespace: {YamlValue.String(ns)}");
        EmitLabels(w, name);
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("podSelector:");
        w.Indent();
        w.Line("matchLabels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Outdent();
        w.Outdent();
        w.Line("policyTypes:");
        w.Indent();
        w.Line("- Ingress");
        w.Line("- Egress");
        w.Outdent();
        w.Raw("""
              egress:
                - to:
                    - namespaceSelector:
                        matchLabels:
                          kubernetes.io/metadata.name: kube-system
                      podSelector:
                        matchLabels:
                          k8s-app: kube-dns
                  ports:
                    - protocol: UDP
                      port: 53
                    - protocol: TCP
                      port: 53
              """);
        if (plan.ExposedPorts.Count > 0)
        {
            w.Line("ingress:");
            w.Indent();
            w.Line("- ports:");
            w.Indent();
            foreach (var port in plan.ExposedPorts)
            {
                w.Line("- protocol: TCP");
                w.Indent();
                w.Line($"port: {port}");
                w.Outdent();
            }
            w.Outdent();
            w.Outdent();
        }
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static void EmitLabels(IndentedTextWriter w, string name)
    {
        w.Line("labels:");
        w.Indent();
        w.Line($"app.kubernetes.io/name: {YamlValue.String(name)}");
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Outdent();
    }

    private static void EmitPodSecurityContext(IndentedTextWriter w, SecurityHardening sec)
    {
        w.Line("securityContext:");
        w.Indent();
        w.Line($"runAsNonRoot: {YamlValue.Bool(sec.RunAsNonRoot)}");
        w.Line($"runAsUser: {sec.RunAsUser}");
        w.Line($"runAsGroup: {sec.RunAsGroup}");
        w.Line($"fsGroup: {sec.RunAsGroup}");
        w.Line("seccompProfile:");
        w.Indent();
        w.Line("type: RuntimeDefault");
        w.Outdent();
        w.Outdent();
    }

    private static void EmitContainer(IndentedTextWriter w, BuildPlan plan, GenerationOptions options, string name)
    {
        w.Line($"- name: {YamlValue.String(name)}");
        w.Indent();
        w.Line($"image: {YamlValue.String(plan.FullImageReference)}");
        w.Line("imagePullPolicy: IfNotPresent");
        EmitContainerSecurityContext(w, plan.Security);
        if (plan.ExposedPorts.Count > 0)
        {
            w.Line("ports:");
            w.Indent();
            foreach (var port in plan.ExposedPorts)
            {
                w.Line($"- name: port-{port}");
                w.Indent();
                w.Line($"containerPort: {port}");
                w.Line("protocol: TCP");
                w.Outdent();
            }
            w.Outdent();
        }
        if (plan.EnvironmentVariables.Count > 0)
        {
            w.Line("env:");
            w.Indent();
            foreach (var env in plan.EnvironmentVariables)
            {
                w.Line($"- name: {YamlValue.String(env.Key)}");
                w.Indent();
                w.Line($"value: {YamlValue.String(env.Value)}");
                w.Outdent();
            }
            w.Outdent();
        }
        w.Line("resources:");
        w.Indent();
        w.Line("requests:");
        w.Indent();
        w.Line($"cpu: {YamlValue.String(options.CpuRequest ?? "100m")}");
        w.Line($"memory: {YamlValue.String(options.MemoryRequest ?? "128Mi")}");
        w.Outdent();
        w.Line("limits:");
        w.Indent();
        w.Line($"cpu: {YamlValue.String(options.CpuLimit ?? "1000m")}");
        w.Line($"memory: {YamlValue.String(options.MemoryLimit ?? "512Mi")}");
        w.Outdent();
        w.Outdent();
        if (plan.Health is { Kind: HealthProbeKind.HttpGet, Port: not null, HttpPath: not null })
        {
            EmitHttpProbe(w, "livenessProbe", plan.Health, initialDelay: 15, period: 20, timeout: 3, failureThreshold: 3);
            EmitHttpProbe(w, "readinessProbe", plan.Health, initialDelay: 5, period: 10, timeout: 2, failureThreshold: 3);
            EmitHttpProbe(w, "startupProbe", plan.Health, initialDelay: 5, period: 5, timeout: 2, failureThreshold: 30);
        }
        if (plan.Security.WritableMounts.Count > 0)
        {
            w.Line("volumeMounts:");
            w.Indent();
            for (int i = 0; i < plan.Security.WritableMounts.Count; i++)
            {
                w.Line($"- name: writable-{i}");
                w.Indent();
                w.Line($"mountPath: {YamlValue.String(plan.Security.WritableMounts[i])}");
                w.Outdent();
            }
            w.Outdent();
        }
        w.Outdent();
    }

    private static void EmitContainerSecurityContext(IndentedTextWriter w, SecurityHardening sec)
    {
        w.Line("securityContext:");
        w.Indent();
        w.Line($"allowPrivilegeEscalation: {YamlValue.Bool(sec.AllowPrivilegeEscalation)}");
        w.Line($"readOnlyRootFilesystem: {YamlValue.Bool(sec.ReadOnlyRootFilesystem)}");
        w.Line($"runAsNonRoot: {YamlValue.Bool(sec.RunAsNonRoot)}");
        w.Line($"runAsUser: {sec.RunAsUser}");
        w.Line($"runAsGroup: {sec.RunAsGroup}");
        w.Line("capabilities:");
        w.Indent();
        w.Line("drop:");
        w.Indent();
        foreach (var cap in sec.DroppedCapabilities)
        {
            w.Line($"- {cap}");
        }
        w.Outdent();
        w.Outdent();
        w.Outdent();
    }

    private static void EmitHttpProbe(
        IndentedTextWriter w,
        string name,
        HealthProbe probe,
        int initialDelay,
        int period,
        int timeout,
        int failureThreshold)
    {
        w.Line($"{name}:");
        w.Indent();
        w.Line("httpGet:");
        w.Indent();
        w.Line($"path: {YamlValue.String(probe.HttpPath!)}");
        w.Line($"port: {probe.Port}");
        w.Outdent();
        w.Line($"initialDelaySeconds: {initialDelay}");
        w.Line($"periodSeconds: {period}");
        w.Line($"timeoutSeconds: {timeout}");
        w.Line($"failureThreshold: {failureThreshold}");
        w.Outdent();
    }
}
