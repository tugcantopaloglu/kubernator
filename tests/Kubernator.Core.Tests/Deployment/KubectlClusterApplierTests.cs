using Kubernator.Core.Deployment;
using Kubernator.Core.Validation;

namespace Kubernator.Core.Tests.Deployment;

public sealed class KubectlClusterApplierTests
{
    [Fact]
    public async Task Apply_refuses_production_context_without_AllowProduction()
    {
        var fake = new RecordingProcessRunner();
        var applier = new KubectlClusterApplier(fake);
        using var t = Kubernator.Core.Tests.Fixtures.TempPublishOutput.Create();
        Directory.CreateDirectory(t.Path);

        var result = await applier.ApplyAsync(new DeployOptions
        {
            ManifestsDirectory = t.Path,
            Context = "prod-eu",
            Namespace = "default"
        });

        result.Ok.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("looks like a production cluster");
        fake.Invocations.Should().BeEmpty(because: "we must not even talk to kubectl when the production guard fires");
    }

    [Fact]
    public async Task Apply_runs_kubectl_with_dry_run_flag()
    {
        var fake = new RecordingProcessRunner();
        fake.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "deployment.apps/demo configured (server dry run)", StandardError = "", Duration = TimeSpan.Zero };
        var applier = new KubectlClusterApplier(fake);
        using var t = Kubernator.Core.Tests.Fixtures.TempPublishOutput.Create();

        var result = await applier.ApplyAsync(new DeployOptions
        {
            ManifestsDirectory = t.Path,
            Context = "kind-foo",
            Namespace = "demo",
            DryRun = true,
            CreateNamespace = false
        });

        result.Ok.Should().BeTrue();
        result.DryRun.Should().BeTrue();
        var apply = fake.Invocations.Last();
        apply.FileName.Should().Be("kubectl");
        apply.Arguments.Should().Contain("apply");
        apply.Arguments.Should().Contain("--dry-run=server");
        apply.Arguments.Should().Contain("kind-foo");
        apply.Arguments.Should().Contain("demo");
    }

    [Fact]
    public async Task Apply_dry_run_creates_namespace_with_client_dry_run()
    {
        var fake = new RecordingProcessRunner();
        fake.Default = new ProcessOutcome { ExitCode = 0, StandardOutput = "ok", StandardError = "", Duration = TimeSpan.Zero };
        var applier = new KubectlClusterApplier(fake);
        using var t = Kubernator.Core.Tests.Fixtures.TempPublishOutput.Create();

        await applier.ApplyAsync(new DeployOptions
        {
            ManifestsDirectory = t.Path,
            Context = "kind-foo",
            Namespace = "demo",
            DryRun = true,
            CreateNamespace = true
        });

        var nsCall = fake.Invocations.FirstOrDefault(i => i.Arguments.Contains("create") && i.Arguments.Contains("namespace"));
        nsCall.Should().NotBeNull(because: "namespace ensure must run even in dry-run so apply --dry-run=server has the namespace");
        nsCall!.Arguments.Should().Contain("--dry-run=client", because: "dry-run mode must not actually mutate the cluster");
    }

    [Fact]
    public async Task Apply_treats_AlreadyExists_namespace_as_success()
    {
        var fake = new RecordingProcessRunner();
        fake.Queue.Enqueue(new ProcessOutcome { ExitCode = 1, StandardOutput = "", StandardError = "namespaces \"demo\" already exists", Duration = TimeSpan.Zero });
        fake.Queue.Enqueue(new ProcessOutcome { ExitCode = 0, StandardOutput = "deployment.apps/demo created", StandardError = "", Duration = TimeSpan.Zero });
        var applier = new KubectlClusterApplier(fake);
        using var t = Kubernator.Core.Tests.Fixtures.TempPublishOutput.Create();

        var result = await applier.ApplyAsync(new DeployOptions
        {
            ManifestsDirectory = t.Path,
            Context = "kind-foo",
            Namespace = "demo",
            CreateNamespace = true
        });

        result.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task ListContexts_parses_kubectl_output_and_marks_current()
    {
        var fake = new RecordingProcessRunner();
        fake.Queue.Enqueue(new ProcessOutcome { ExitCode = 0, StandardOutput = "kind-kubernator-test\nprod-eu\ntest-eu\n", StandardError = "", Duration = TimeSpan.Zero });
        fake.Queue.Enqueue(new ProcessOutcome { ExitCode = 0, StandardOutput = "test-eu\n", StandardError = "", Duration = TimeSpan.Zero });
        var applier = new KubectlClusterApplier(fake);

        var contexts = await applier.ListContextsAsync();

        contexts.Should().HaveCount(3);
        contexts.Single(c => c.IsCurrent).Name.Should().Be("test-eu");
        contexts.Single(c => c.Name == "prod-eu").LooksProduction.Should().BeTrue();
        contexts.Single(c => c.Name == "test-eu").LooksProduction.Should().BeFalse();
    }

    [Theory]
    [InlineData("prod-eu", true)]
    [InlineData("eu-production", true)]
    [InlineData("live-cluster", true)]
    [InlineData("kind-test", false)]
    [InlineData("staging-eu", false)]
    [InlineData("dev", false)]
    public void LooksLikeProduction_classification(string name, bool expected)
    {
        ClusterContext.LooksLikeProduction(name).Should().Be(expected);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessInvocation> Invocations { get; } = [];
        public Queue<ProcessOutcome> Queue { get; } = new();
        public ProcessOutcome Default { get; set; } = new()
        {
            ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero
        };

        public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default)
        {
            Invocations.Add(invocation);
            return Task.FromResult(Queue.Count > 0 ? Queue.Dequeue() : Default);
        }
    }
}
