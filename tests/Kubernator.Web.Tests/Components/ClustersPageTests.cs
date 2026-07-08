using Bunit;
using Kubernator.Core.ClusterProvisioning.Discovery;
using Kubernator.Core.Deployment;
using Kubernator.Core.Validation;
using Kubernator.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Kubernator.Web.Tests.Components;

/// <summary>
/// First Blazor component tests in the repo — bootstraps the bUnit harness and exercises the
/// interactive "discover topology" flow added to the clusters page end-to-end (form input →
/// <see cref="ClusterTopologyDiscoverer"/> → rendered nodes), which has no REST-level coverage.
/// </summary>
public sealed class ClustersPageTests : BunitContext
{
    private const string TwoNodeKubectlJson = """
        {
          "items": [
            {
              "metadata": {
                "name": "m1",
                "labels": { "node-role.kubernetes.io/control-plane": "" },
                "creationTimestamp": "2024-01-01T00:00:00Z"
              },
              "status": { "addresses": [ { "type": "InternalIP", "address": "10.0.0.1" } ] }
            },
            {
              "metadata": {
                "name": "w1",
                "labels": {},
                "creationTimestamp": "2024-01-02T00:00:00Z"
              },
              "status": { "addresses": [ { "type": "InternalIP", "address": "10.0.0.2" } ] }
            }
          ]
        }
        """;

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly string stdout;
        public StubProcessRunner(string stdout) => this.stdout = stdout;

        public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default) =>
            Task.FromResult(new ProcessOutcome { ExitCode = 0, StandardOutput = stdout, StandardError = "", Duration = TimeSpan.Zero });
    }

    private void RegisterServices(IProcessRunner runner)
    {
        var applier = Substitute.For<IClusterApplier>();
        applier.ListContextsAsync().Returns(Task.FromResult<IReadOnlyList<ClusterContext>>([]));
        Services.AddSingleton(applier);
        Services.AddSingleton(new ClusterTopologyDiscoverer(runner));
    }

    [Fact]
    public void Discover_section_renders()
    {
        RegisterServices(new StubProcessRunner(TwoNodeKubectlJson));

        var cut = Render<Clusters>();

        cut.Markup.Should().Contain("discover topology");
        cut.Find("#dc-name").Should().NotBeNull();
    }

    [Fact]
    public void Discovering_renders_the_reverse_mapped_nodes()
    {
        RegisterServices(new StubProcessRunner(TwoNodeKubectlJson));

        var cut = Render<Clusters>();
        cut.Find("#dc-name").Change("demo");
        cut.Find("#dc-version").Change("v1.30.4+rke2r1");
        cut.Find("#dc-user").Change("root");

        // The discover form is the first EditForm on the page.
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("m1");
            cut.Markup.Should().Contain("w1");
            cut.Markup.Should().Contain("init");
            cut.Markup.Should().Contain("topology.json");
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Discovery_failure_surfaces_an_error_banner()
    {
        RegisterServices(new StubProcessRunner("{ not valid json"));

        var cut = Render<Clusters>();
        cut.Find("#dc-name").Change("demo");
        cut.Find("#dc-version").Change("v1.30.4+rke2r1");
        cut.Find("#dc-user").Change("root");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.FindAll(".banner.err").Should().NotBeEmpty(), TimeSpan.FromSeconds(5));
    }
}
