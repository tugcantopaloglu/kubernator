using Kubernator.Core.Validation;

namespace Kubernator.Core.Tests.Validation;

public sealed class KindValidatorTests
{
    [Fact]
    public async Task Bails_when_kind_version_fails()
    {
        var runner = new ScriptedProcessRunner();
        runner.Enqueue("kind", ["version"], exit: 1, stderr: "kind not found");

        var validator = new KindValidator(runner);
        var result = await validator.ValidateAsync(new ValidationOptions
        {
            ManifestsDirectory = "/tmp/m",
            ImageReference = "demo:0.1.0",
            DeploymentName = "demo"
        });

        result.Ok.Should().BeFalse();
        result.Steps.Should().HaveCount(1);
        result.Steps[0].Name.Should().Be("kind version");
        result.Steps[0].Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Happy_path_runs_create_load_apply_wait_delete()
    {
        var runner = new ScriptedProcessRunner();
        runner.Enqueue("kind", ["version"], exit: 0);
        runner.Enqueue("kind", ["create", "cluster", "--name", "ctest", "--wait", "60s"], exit: 0);
        runner.Enqueue("kind", ["load", "docker-image", "demo:1.0", "--name", "ctest"], exit: 0);
        runner.Enqueue("kubectl", ["apply", "-f", "/tmp/m", "--context", "kind-ctest", "-n", "default"], exit: 0);
        runner.Enqueue("kubectl",
            ["wait", "--for=condition=available", "--timeout", "120s", "deployment", "demo", "--context", "kind-ctest", "-n", "default"],
            exit: 0);
        runner.Enqueue("kind", ["delete", "cluster", "--name", "ctest"], exit: 0);

        var validator = new KindValidator(runner);
        var result = await validator.ValidateAsync(new ValidationOptions
        {
            ManifestsDirectory = "/tmp/m",
            ImageReference = "demo:1.0",
            DeploymentName = "demo",
            ClusterName = "ctest"
        });

        result.Ok.Should().BeTrue();
        result.Steps.Should().Contain(s => s.Name.StartsWith("kind create cluster", StringComparison.Ordinal) && s.Ok);
        result.Steps.Should().Contain(s => s.Name == "kind load docker-image" && s.Ok);
        result.Steps.Should().Contain(s => s.Name == "kubectl apply" && s.Ok);
        result.Steps.Should().Contain(s => s.Name == "kubectl wait deployment ready" && s.Ok);
        result.Steps.Should().Contain(s => s.Name.StartsWith("kind delete cluster", StringComparison.Ordinal) && s.Ok);
    }

    [Fact]
    public async Task Skips_create_when_reusing_cluster()
    {
        var runner = new ScriptedProcessRunner();
        runner.Enqueue("kind", ["version"], exit: 0);
        runner.Enqueue("kind", ["load", "docker-image", "demo:1.0", "--name", "existing"], exit: 0);
        runner.Enqueue("kubectl", ["apply", "-f", "/tmp/m", "--context", "kind-existing", "-n", "default"], exit: 0);
        runner.Enqueue("kubectl",
            ["wait", "--for=condition=available", "--timeout", "120s", "deployment", "demo", "--context", "kind-existing", "-n", "default"],
            exit: 0);

        var validator = new KindValidator(runner);
        var result = await validator.ValidateAsync(new ValidationOptions
        {
            ManifestsDirectory = "/tmp/m",
            ImageReference = "demo:1.0",
            DeploymentName = "demo",
            ClusterName = "existing",
            ReuseExistingCluster = true,
            DeleteClusterOnComplete = false
        });

        result.Ok.Should().BeTrue();
        result.Steps.Should().NotContain(s => s.Name.StartsWith("kind create", StringComparison.Ordinal));
        result.Steps.Should().NotContain(s => s.Name.StartsWith("kind delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_kubectl_apply_failure_and_stops()
    {
        var runner = new ScriptedProcessRunner();
        runner.Enqueue("kind", ["version"], exit: 0);
        runner.Enqueue("kind", ["create", "cluster", "--name", "ctest", "--wait", "60s"], exit: 0);
        runner.Enqueue("kind", ["load", "docker-image", "demo:1.0", "--name", "ctest"], exit: 0);
        runner.Enqueue("kubectl", ["apply", "-f", "/tmp/m", "--context", "kind-ctest", "-n", "default"], exit: 1, stderr: "manifest invalid");
        runner.Enqueue("kind", ["delete", "cluster", "--name", "ctest"], exit: 0);

        var validator = new KindValidator(runner);
        var result = await validator.ValidateAsync(new ValidationOptions
        {
            ManifestsDirectory = "/tmp/m",
            ImageReference = "demo:1.0",
            DeploymentName = "demo",
            ClusterName = "ctest"
        });

        result.Ok.Should().BeFalse();
        result.Steps.Should().Contain(s => s.Name == "kubectl apply" && !s.Ok);
        result.Steps.Should().NotContain(s => s.Name == "kubectl wait deployment ready");
    }
}

internal sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly Queue<(string FileName, string[] Arguments, int Exit, string Stdout, string Stderr)> queue = new();

    public void Enqueue(string fileName, string[] arguments, int exit = 0, string stdout = "", string stderr = "")
    {
        queue.Enqueue((fileName, arguments, exit, stdout, stderr));
    }

    public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default)
    {
        if (queue.Count == 0)
        {
            return Task.FromResult(new ProcessOutcome
            {
                ExitCode = -42,
                StandardOutput = string.Empty,
                StandardError = $"unexpected invocation: {invocation.FileName} {string.Join(' ', invocation.Arguments)}",
                Duration = TimeSpan.Zero
            });
        }
        var entry = queue.Dequeue();
        return Task.FromResult(new ProcessOutcome
        {
            ExitCode = entry.Exit,
            StandardOutput = entry.Stdout,
            StandardError = entry.Stderr,
            Duration = TimeSpan.FromMilliseconds(1)
        });
    }
}
