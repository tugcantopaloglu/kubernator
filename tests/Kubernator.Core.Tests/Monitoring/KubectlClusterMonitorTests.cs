using Kubernator.Core.Monitoring;
using Kubernator.Core.Validation;

namespace Kubernator.Core.Tests.Monitoring;

public class KubectlClusterMonitorTests
{
    [Fact]
    public async Task Snapshot_ParsesNodesAndStatus()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["version", "-o", "json"], """
            {"serverVersion":{"gitVersion":"v1.30.0"}}
            """);
        runner.Map(["get", "nodes", "-o", "json"], NodesJson);
        runner.Map(["get", "pods", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "ingress", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "networkpolicy", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["top", "nodes", "--no-headers"], "control-plane  120m  6%  500Mi  10%\nworker-1  300m  15%  1Gi  20%");
        runner.Map(["top", "pods", "--no-headers", "--all-namespaces"], "");

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "kind-test" });

        snap.Context.Should().Be("kind-test");
        snap.ApiVersion.Should().Be("v1.30.0");
        snap.Nodes.Should().HaveCount(2);
        var ctrl = snap.Nodes.Single(n => n.Name == "control-plane");
        ctrl.Status.Should().Be("Ready");
        ctrl.Roles.Should().Contain("control-plane");
        ctrl.KubeletVersion.Should().Be("v1.30.0");
        ctrl.Allocatable.Cpu.Should().Be("4");
        ctrl.Allocatable.Memory.Should().Be("8Gi");
        ctrl.Usage.Should().NotBeNull();
        ctrl.Usage!.Cpu.Should().Be("120m");
        ctrl.Usage.Memory.Should().Be("500Mi");
    }

    [Fact]
    public async Task Snapshot_DetectsNotReadyNode()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["version", "-o", "json"], """
            {"serverVersion":{"gitVersion":"v1.30.0"}}
            """);
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {
                "metadata":{"name":"bad","labels":{},"creationTimestamp":"2026-04-01T00:00:00Z"},
                "status":{
                  "allocatable":{"cpu":"2","memory":"4Gi","pods":"110"},
                  "nodeInfo":{"kubeletVersion":"v1.30.0","osImage":"linux","architecture":"amd64"},
                  "conditions":[{"type":"Ready","status":"False","reason":"KubeletNotReady"}]
                }
              }
            ]}
            """);
        runner.Map(["get", "pods", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "ingress", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "networkpolicy", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "ctx", IncludeMetrics = false });

        snap.Nodes.Single().Status.Should().Be("NotReady");
        snap.ReadyNodes.Should().Be(0);
    }

    [Fact]
    public async Task Snapshot_PodsCountByPhase()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], EmptyList);
        runner.Map(["get", "pods", "--all-namespaces", "-o", "json"], """
            {"items":[
              {"metadata":{"namespace":"a","name":"p1","creationTimestamp":"2026-04-01T00:00:00Z"},"spec":{"nodeName":"n1"},"status":{"phase":"Running","containerStatuses":[{"ready":true,"restartCount":0}]}},
              {"metadata":{"namespace":"a","name":"p2","creationTimestamp":"2026-04-01T00:00:00Z"},"spec":{"nodeName":"n1"},"status":{"phase":"Failed","containerStatuses":[{"ready":false,"restartCount":3}]}},
              {"metadata":{"namespace":"a","name":"p3","creationTimestamp":"2026-04-01T00:00:00Z"},"spec":{"nodeName":"n1"},"status":{"phase":"Running","containerStatuses":[{"ready":true,"restartCount":0},{"ready":true,"restartCount":0}]}}
            ]}
            """);
        runner.Map(["get", "ingress", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "networkpolicy", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "ctx", IncludeMetrics = false });

        snap.RunningPods.Should().Be(2);
        snap.FailedPods.Should().Be(1);
        var failed = snap.Pods.Single(p => p.Phase == "Failed");
        failed.Restarts.Should().Be(3);
    }

    [Fact]
    public async Task Snapshot_ParsesIngressHostsAndAddresses()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], EmptyList);
        runner.Map(["get", "pods", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "ingress", "--all-namespaces", "-o", "json"], """
            {"items":[
              {
                "metadata":{"namespace":"prod","name":"app","creationTimestamp":"2026-04-01T00:00:00Z"},
                "spec":{
                  "ingressClassName":"nginx",
                  "rules":[{"host":"app.example.com"},{"host":"api.example.com"}],
                  "tls":[{"hosts":["app.example.com"]}]
                },
                "status":{"loadBalancer":{"ingress":[{"ip":"10.0.0.1"},{"hostname":"lb.example.com"}]}}
              }
            ]}
            """);
        runner.Map(["get", "networkpolicy", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "ctx", IncludeMetrics = false });

        var ing = snap.Ingresses.Single();
        ing.IngressClass.Should().Be("nginx");
        ing.Hosts.Should().BeEquivalentTo(ExpectedHosts);
        ing.TlsHosts.Should().ContainSingle().Which.Should().Be("app.example.com");
        ing.Addresses.Should().BeEquivalentTo(ExpectedAddresses);
    }

    [Fact]
    public async Task Snapshot_HandlesMetricsServerUnavailable()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], EmptyList);
        runner.Map(["get", "pods", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "ingress", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "networkpolicy", "--all-namespaces", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "--all-namespaces", "-o", "json"], EmptyList);
        runner.MapFailure(["top", "nodes", "--no-headers"], "error: Metrics API not available");

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "ctx" });

        snap.MetricsServerAvailable.Should().BeFalse();
        snap.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Snapshot_NamespaceFilterUsesDashN()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], EmptyList);
        runner.Map(["get", "pods", "-n", "shop", "-o", "json"], EmptyList);
        runner.Map(["get", "ingress", "-n", "shop", "-o", "json"], EmptyList);
        runner.Map(["get", "networkpolicy", "-n", "shop", "-o", "json"], EmptyList);
        runner.Map(["get", "services", "-n", "shop", "-o", "json"], EmptyList);
        runner.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };

        var sut = new KubectlClusterMonitor(runner);
        await sut.GetSnapshotAsync(new ClusterMonitorOptions { Context = "ctx", Namespace = "shop", IncludeMetrics = false });

        runner.Invocations.Should().Contain(i =>
            i.Arguments.Contains("get") && i.Arguments.Contains("pods")
            && i.Arguments.Contains("-n") && i.Arguments.Contains("shop"));
        runner.Invocations.Should().NotContain(i => i.Arguments.Contains("--all-namespaces") && i.Arguments.Contains("pods"));
    }

    [Fact]
    public async Task Snapshot_SkipsDisabledSections()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], EmptyList);
        runner.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = EmptyList, StandardError = "", Duration = TimeSpan.Zero };

        var sut = new KubectlClusterMonitor(runner);
        var snap = await sut.GetSnapshotAsync(new ClusterMonitorOptions
        {
            Context = "ctx",
            IncludePods = false,
            IncludeIngress = false,
            IncludeNetworkPolicies = false,
            IncludeServices = false,
            IncludeMetrics = false
        });

        snap.Pods.Should().BeEmpty();
        snap.Ingresses.Should().BeEmpty();
        snap.NetworkPolicies.Should().BeEmpty();
        snap.Services.Should().BeEmpty();

        runner.Invocations.Should().NotContain(i => i.Arguments.Contains("pods") && !i.Arguments.Contains("top"));
    }

    private static readonly string[] ExpectedHosts = ["app.example.com", "api.example.com"];
    private static readonly string[] ExpectedAddresses = ["10.0.0.1", "lb.example.com"];

    private const string EmptyList = """{"items":[]}""";

    private const string NodesJson = """
        {"items":[
          {
            "metadata":{
              "name":"control-plane",
              "labels":{"node-role.kubernetes.io/control-plane":""},
              "creationTimestamp":"2026-04-01T00:00:00Z"
            },
            "status":{
              "allocatable":{"cpu":"4","memory":"8Gi","pods":"110"},
              "nodeInfo":{"kubeletVersion":"v1.30.0","osImage":"Ubuntu 22.04","architecture":"amd64"},
              "conditions":[{"type":"Ready","status":"True"}]
            }
          },
          {
            "metadata":{
              "name":"worker-1",
              "labels":{"node-role.kubernetes.io/worker":""},
              "creationTimestamp":"2026-04-01T00:00:00Z"
            },
            "status":{
              "allocatable":{"cpu":"8","memory":"16Gi","pods":"110"},
              "nodeInfo":{"kubeletVersion":"v1.30.0","osImage":"Ubuntu 22.04","architecture":"amd64"},
              "conditions":[{"type":"Ready","status":"True"}]
            }
          }
        ]}
        """;
}

internal sealed class ScriptedProcessRunner : IProcessRunner
{
    public List<ProcessInvocation> Invocations { get; } = [];
    public ProcessOutcome Default { get; set; } = new()
    {
        ExitCode = 1, StandardOutput = "", StandardError = "no script for this invocation", Duration = TimeSpan.Zero
    };

    private readonly List<(IReadOnlyList<string> Match, ProcessOutcome Outcome)> scripts = [];

    public void Map(IReadOnlyList<string> argTokens, string stdout)
    {
        scripts.Add((argTokens, new ProcessOutcome { ExitCode = 0, StandardOutput = stdout, StandardError = "", Duration = TimeSpan.Zero }));
    }

    public void MapFailure(IReadOnlyList<string> argTokens, string stderr)
    {
        scripts.Add((argTokens, new ProcessOutcome { ExitCode = 1, StandardOutput = "", StandardError = stderr, Duration = TimeSpan.Zero }));
    }

    public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default)
    {
        Invocations.Add(invocation);
        foreach (var (match, outcome) in scripts)
        {
            if (Matches(invocation.Arguments, match))
            {
                return Task.FromResult(outcome);
            }
        }
        return Task.FromResult(Default);
    }

    private static bool Matches(IReadOnlyList<string> actual, IReadOnlyList<string> needle)
    {
        foreach (var token in needle)
        {
            if (!actual.Contains(token)) return false;
        }
        return true;
    }
}
