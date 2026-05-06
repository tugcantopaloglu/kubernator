using System.Globalization;
using System.Text.Json;
using Kubernator.Core.Validation;

namespace Kubernator.Core.Monitoring;

public sealed class KubectlClusterMonitor : IClusterMonitor
{
    private readonly IProcessRunner runner;

    public KubectlClusterMonitor(IProcessRunner runner)
    {
        this.runner = runner;
    }

    public async Task<ClusterSnapshot> GetSnapshotAsync(ClusterMonitorOptions options, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var apiVersion = await GetServerVersionAsync(options, ct);

        var nodes = await GetNodesAsync(options, now, warnings, ct);
        var pods = options.IncludePods ? await GetPodsAsync(options, now, warnings, ct) : [];
        var ingresses = options.IncludeIngress ? await GetIngressesAsync(options, now, warnings, ct) : [];
        var policies = options.IncludeNetworkPolicies ? await GetNetworkPoliciesAsync(options, now, warnings, ct) : [];
        var services = options.IncludeServices ? await GetServicesAsync(options, now, warnings, ct) : [];

        var metricsAvailable = false;
        IReadOnlyDictionary<string, ResourceQty> nodeMetrics = new Dictionary<string, ResourceQty>();
        IReadOnlyDictionary<string, ResourceQty> podMetrics = new Dictionary<string, ResourceQty>();
        if (options.IncludeMetrics)
        {
            (metricsAvailable, nodeMetrics, podMetrics) = await GetMetricsAsync(options, warnings, ct);
        }

        if (metricsAvailable)
        {
            nodes = nodes.Select(n => nodeMetrics.TryGetValue(n.Name, out var u)
                ? n with { Usage = u }
                : n).ToList();
            pods = pods.Select(p => podMetrics.TryGetValue(p.Namespace + "/" + p.Name, out var u)
                ? p with { Usage = u }
                : p).ToList();
        }

        return new ClusterSnapshot
        {
            Context = options.Context,
            CapturedAt = now,
            Nodes = nodes,
            Pods = pods,
            Ingresses = ingresses,
            NetworkPolicies = policies,
            Services = services,
            MetricsServerAvailable = metricsAvailable,
            Warnings = warnings,
            ApiVersion = apiVersion
        };
    }

    private async Task<string?> GetServerVersionAsync(ClusterMonitorOptions options, CancellationToken ct)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(options.Context))
        {
            args.Add("--context");
            args.Add(options.Context);
        }
        args.AddRange(["version", "-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            if (doc.RootElement.TryGetProperty("serverVersion", out var sv))
            {
                return sv.TryGetProperty("gitVersion", out var gv) ? gv.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private async Task<List<NodeStatus>> GetNodesAsync(ClusterMonitorOptions options, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var args = ContextArgs(options);
        args.AddRange(["get", "nodes", "-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            warnings.Add($"nodes: {outcome.StandardError.Trim()}");
            return [];
        }
        var list = new List<NodeStatus>();
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            foreach (var item in EnumerateItems(doc.RootElement))
            {
                var meta = item.GetProperty("metadata");
                var status = item.GetProperty("status");
                var name = meta.GetProperty("name").GetString() ?? "";
                var creation = ParseTimestamp(meta);
                var labels = ReadLabels(meta);

                var nodeInfo = status.TryGetProperty("nodeInfo", out var ni) ? ni : default;
                var allocatableObj = status.TryGetProperty("allocatable", out var allocEl) ? allocEl : default;

                var conditions = new List<NodeCondition>();
                var statusName = "NotReady";
                if (status.TryGetProperty("conditions", out var condArr) && condArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cond in condArr.EnumerateArray())
                    {
                        var type = cond.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        var s = cond.TryGetProperty("status", out var sv) ? sv.GetString() ?? "" : "";
                        conditions.Add(new NodeCondition
                        {
                            Type = type,
                            Status = s,
                            Reason = cond.TryGetProperty("reason", out var r) ? r.GetString() : null,
                            Message = cond.TryGetProperty("message", out var m) ? m.GetString() : null
                        });
                        if (string.Equals(type, "Ready", StringComparison.Ordinal) && string.Equals(s, "True", StringComparison.Ordinal))
                        {
                            statusName = "Ready";
                        }
                    }
                }

                var roles = ExtractRoles(labels);

                list.Add(new NodeStatus
                {
                    Name = name,
                    Status = statusName,
                    Roles = roles,
                    KubeletVersion = nodeInfo.ValueKind == JsonValueKind.Object && nodeInfo.TryGetProperty("kubeletVersion", out var kv) ? kv.GetString() ?? "" : "",
                    OsImage = nodeInfo.ValueKind == JsonValueKind.Object && nodeInfo.TryGetProperty("osImage", out var osi) ? osi.GetString() ?? "" : "",
                    Architecture = nodeInfo.ValueKind == JsonValueKind.Object && nodeInfo.TryGetProperty("architecture", out var arch) ? arch.GetString() ?? "" : "",
                    Allocatable = ReadResourceQty(allocatableObj),
                    Conditions = conditions,
                    Age = creation is null ? TimeSpan.Zero : now - creation.Value,
                    Labels = labels
                });
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"nodes parse: {ex.Message}");
        }
        return list;
    }

    private async Task<List<PodStatus>> GetPodsAsync(ClusterMonitorOptions options, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var args = ContextArgs(options);
        args.AddRange(["get", "pods"]);
        AppendNamespaceArgs(options, args);
        args.AddRange(["-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            warnings.Add($"pods: {outcome.StandardError.Trim()}");
            return [];
        }
        var list = new List<PodStatus>();
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            foreach (var item in EnumerateItems(doc.RootElement))
            {
                var meta = item.GetProperty("metadata");
                var status = item.GetProperty("status");
                var spec = item.GetProperty("spec");
                var ns = meta.GetProperty("namespace").GetString() ?? "";
                var name = meta.GetProperty("name").GetString() ?? "";
                var phase = status.TryGetProperty("phase", out var ph) ? ph.GetString() ?? "" : "";
                var node = spec.TryGetProperty("nodeName", out var n) ? n.GetString() ?? "" : "";

                int containersTotal = 0;
                int containersReady = 0;
                int restarts = 0;
                if (status.TryGetProperty("containerStatuses", out var cs) && cs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cs.EnumerateArray())
                    {
                        containersTotal++;
                        if (c.TryGetProperty("ready", out var ready) && ready.GetBoolean())
                        {
                            containersReady++;
                        }
                        if (c.TryGetProperty("restartCount", out var rc) && rc.TryGetInt32(out var rci))
                        {
                            restarts += rci;
                        }
                    }
                }

                var creation = ParseTimestamp(meta);
                list.Add(new PodStatus
                {
                    Namespace = ns,
                    Name = name,
                    Phase = phase,
                    NodeName = node,
                    Restarts = restarts,
                    ContainersReady = containersReady,
                    ContainersTotal = containersTotal,
                    Age = creation is null ? TimeSpan.Zero : now - creation.Value
                });
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"pods parse: {ex.Message}");
        }
        return list;
    }

    private async Task<List<IngressStatus>> GetIngressesAsync(ClusterMonitorOptions options, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var args = ContextArgs(options);
        args.AddRange(["get", "ingress"]);
        AppendNamespaceArgs(options, args);
        args.AddRange(["-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            if (!outcome.StandardError.Contains("No resources found", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"ingresses: {outcome.StandardError.Trim()}");
            }
            return [];
        }
        var list = new List<IngressStatus>();
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            foreach (var item in EnumerateItems(doc.RootElement))
            {
                var meta = item.GetProperty("metadata");
                var spec = item.GetProperty("spec");
                var ns = meta.GetProperty("namespace").GetString() ?? "";
                var name = meta.GetProperty("name").GetString() ?? "";
                var className = spec.TryGetProperty("ingressClassName", out var ic) ? ic.GetString() ?? "" : "";

                var hosts = new List<string>();
                var tlsHosts = new List<string>();
                if (spec.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        if (rule.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.String)
                        {
                            hosts.Add(h.GetString()!);
                        }
                    }
                }
                if (spec.TryGetProperty("tls", out var tlsArr) && tlsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tls in tlsArr.EnumerateArray())
                    {
                        if (tls.TryGetProperty("hosts", out var hostsArr) && hostsArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var h in hostsArr.EnumerateArray())
                            {
                                if (h.ValueKind == JsonValueKind.String)
                                {
                                    tlsHosts.Add(h.GetString()!);
                                }
                            }
                        }
                    }
                }

                var addresses = new List<string>();
                if (item.TryGetProperty("status", out var st)
                    && st.TryGetProperty("loadBalancer", out var lb)
                    && lb.TryGetProperty("ingress", out var lbIng)
                    && lbIng.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in lbIng.EnumerateArray())
                    {
                        if (entry.TryGetProperty("ip", out var ip) && ip.ValueKind == JsonValueKind.String)
                        {
                            addresses.Add(ip.GetString()!);
                        }
                        else if (entry.TryGetProperty("hostname", out var hn) && hn.ValueKind == JsonValueKind.String)
                        {
                            addresses.Add(hn.GetString()!);
                        }
                    }
                }

                var creation = ParseTimestamp(meta);
                list.Add(new IngressStatus
                {
                    Namespace = ns,
                    Name = name,
                    IngressClass = className,
                    Hosts = hosts,
                    TlsHosts = tlsHosts,
                    Addresses = addresses,
                    Age = creation is null ? TimeSpan.Zero : now - creation.Value
                });
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"ingresses parse: {ex.Message}");
        }
        return list;
    }

    private async Task<List<NetworkPolicyStatus>> GetNetworkPoliciesAsync(ClusterMonitorOptions options, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var args = ContextArgs(options);
        args.AddRange(["get", "networkpolicy"]);
        AppendNamespaceArgs(options, args);
        args.AddRange(["-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            if (!outcome.StandardError.Contains("No resources found", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"networkpolicies: {outcome.StandardError.Trim()}");
            }
            return [];
        }
        var list = new List<NetworkPolicyStatus>();
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            foreach (var item in EnumerateItems(doc.RootElement))
            {
                var meta = item.GetProperty("metadata");
                var spec = item.GetProperty("spec");
                var ns = meta.GetProperty("namespace").GetString() ?? "";
                var name = meta.GetProperty("name").GetString() ?? "";
                var podSelector = spec.TryGetProperty("podSelector", out var ps)
                    ? ps.GetRawText()
                    : "{}";
                var policyTypes = new List<string>();
                if (spec.TryGetProperty("policyTypes", out var pt) && pt.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in pt.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.String)
                        {
                            policyTypes.Add(v.GetString()!);
                        }
                    }
                }
                var creation = ParseTimestamp(meta);
                list.Add(new NetworkPolicyStatus
                {
                    Namespace = ns,
                    Name = name,
                    PodSelector = podSelector,
                    PolicyTypes = policyTypes,
                    Age = creation is null ? TimeSpan.Zero : now - creation.Value
                });
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"networkpolicies parse: {ex.Message}");
        }
        return list;
    }

    private async Task<List<ServiceStatus>> GetServicesAsync(ClusterMonitorOptions options, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var args = ContextArgs(options);
        args.AddRange(["get", "services"]);
        AppendNamespaceArgs(options, args);
        args.AddRange(["-o", "json"]);
        var outcome = await Run(options, args, ct);
        if (!outcome.Ok)
        {
            warnings.Add($"services: {outcome.StandardError.Trim()}");
            return [];
        }
        var list = new List<ServiceStatus>();
        try
        {
            using var doc = JsonDocument.Parse(outcome.StandardOutput);
            foreach (var item in EnumerateItems(doc.RootElement))
            {
                var meta = item.GetProperty("metadata");
                var spec = item.GetProperty("spec");
                var ns = meta.GetProperty("namespace").GetString() ?? "";
                var name = meta.GetProperty("name").GetString() ?? "";
                var type = spec.TryGetProperty("type", out var t) ? t.GetString() ?? "ClusterIP" : "ClusterIP";
                var clusterIp = spec.TryGetProperty("clusterIP", out var cip) ? cip.GetString() ?? "" : "";
                var externalIps = new List<string>();
                if (spec.TryGetProperty("externalIPs", out var eips) && eips.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in eips.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.String)
                        {
                            externalIps.Add(v.GetString()!);
                        }
                    }
                }
                var ports = new List<string>();
                if (spec.TryGetProperty("ports", out var portsArr) && portsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in portsArr.EnumerateArray())
                    {
                        var port = p.TryGetProperty("port", out var pp) && pp.TryGetInt32(out var pi) ? pi.ToString(CultureInfo.InvariantCulture) : "?";
                        var protocol = p.TryGetProperty("protocol", out var pr) ? pr.GetString() : "TCP";
                        ports.Add($"{port}/{protocol}");
                    }
                }
                var creation = ParseTimestamp(meta);
                list.Add(new ServiceStatus
                {
                    Namespace = ns,
                    Name = name,
                    Type = type,
                    ClusterIp = clusterIp,
                    ExternalIps = externalIps,
                    Ports = ports,
                    Age = creation is null ? TimeSpan.Zero : now - creation.Value
                });
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"services parse: {ex.Message}");
        }
        return list;
    }

    private async Task<(bool Available, IReadOnlyDictionary<string, ResourceQty> Nodes, IReadOnlyDictionary<string, ResourceQty> Pods)> GetMetricsAsync(
        ClusterMonitorOptions options,
        List<string> warnings,
        CancellationToken ct)
    {
        var nodeMap = new Dictionary<string, ResourceQty>(StringComparer.Ordinal);
        var podMap = new Dictionary<string, ResourceQty>(StringComparer.Ordinal);

        var nodeArgs = ContextArgs(options);
        nodeArgs.AddRange(["top", "nodes", "--no-headers"]);
        var nodeOutcome = await Run(options, nodeArgs, ct);
        if (!nodeOutcome.Ok)
        {
            if (nodeOutcome.StandardError.Contains("metrics", StringComparison.OrdinalIgnoreCase)
                && nodeOutcome.StandardError.Contains("not", StringComparison.OrdinalIgnoreCase))
            {
                return (false, nodeMap, podMap);
            }
            warnings.Add($"top nodes: {nodeOutcome.StandardError.Trim()}");
            return (false, nodeMap, podMap);
        }

        foreach (var line in nodeOutcome.StandardOutput.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            nodeMap[parts[0]] = new ResourceQty { Cpu = parts[1], Memory = parts[3] };
        }

        var podArgs = ContextArgs(options);
        podArgs.AddRange(["top", "pods", "--no-headers"]);
        AppendNamespaceArgs(options, podArgs);
        if (string.IsNullOrEmpty(options.Namespace))
        {
            podArgs.Add("--all-namespaces");
        }
        var podOutcome = await Run(options, podArgs, ct);
        if (!podOutcome.Ok)
        {
            return (true, nodeMap, podMap);
        }

        var allNs = string.IsNullOrEmpty(options.Namespace);
        foreach (var line in podOutcome.StandardOutput.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            string ns;
            string name;
            string cpu;
            string mem;
            if (allNs)
            {
                if (parts.Length < 4) continue;
                ns = parts[0];
                name = parts[1];
                cpu = parts[2];
                mem = parts[3];
            }
            else
            {
                if (parts.Length < 3) continue;
                ns = options.Namespace!;
                name = parts[0];
                cpu = parts[1];
                mem = parts[2];
            }
            podMap[ns + "/" + name] = new ResourceQty { Cpu = cpu, Memory = mem };
        }

        return (true, nodeMap, podMap);
    }

    private static List<string> ContextArgs(ClusterMonitorOptions options)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(options.Context))
        {
            args.Add("--context");
            args.Add(options.Context);
        }
        return args;
    }

    private static void AppendNamespaceArgs(ClusterMonitorOptions options, List<string> args)
    {
        if (string.IsNullOrEmpty(options.Namespace))
        {
            args.Add("--all-namespaces");
        }
        else
        {
            args.Add("-n");
            args.Add(options.Namespace);
        }
    }

    private async Task<ProcessOutcome> Run(ClusterMonitorOptions options, IReadOnlyList<string> args, CancellationToken ct)
    {
        return await runner.RunAsync(new ProcessInvocation
        {
            FileName = options.KubectlBinary,
            Arguments = args,
            Timeout = options.Timeout
        }, ct);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var item in items.EnumerateArray())
        {
            yield return item;
        }
    }

    private static DateTimeOffset? ParseTimestamp(JsonElement meta)
    {
        if (meta.TryGetProperty("creationTimestamp", out var ts) && ts.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadLabels(JsonElement meta)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (meta.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in labelsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    labels[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }
        return labels;
    }

    private static List<string> ExtractRoles(IReadOnlyDictionary<string, string> labels)
    {
        var roles = new List<string>();
        foreach (var key in labels.Keys)
        {
            const string Prefix = "node-role.kubernetes.io/";
            if (key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                var role = key[Prefix.Length..];
                if (!string.IsNullOrEmpty(role))
                {
                    roles.Add(role);
                }
            }
        }
        return roles.Count == 0 ? ["<none>"] : roles;
    }

    private static ResourceQty ReadResourceQty(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ResourceQty { Cpu = "?", Memory = "?" };
        }
        var cpu = element.TryGetProperty("cpu", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "?" : "?";
        var mem = element.TryGetProperty("memory", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "?" : "?";
        var pods = element.TryGetProperty("pods", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        return new ResourceQty { Cpu = cpu, Memory = mem, Pods = pods };
    }
}
