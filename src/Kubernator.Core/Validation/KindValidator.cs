using System.Diagnostics;

namespace Kubernator.Core.Validation;

public sealed class KindValidator : IValidator
{
    private readonly IProcessRunner runner;

    public KindValidator(IProcessRunner runner)
    {
        this.runner = runner;
    }

    public async Task<ValidationResult> ValidateAsync(
        ValidationOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var steps = new List<ValidationStep>();
        var clusterCreated = false;
        var success = false;
        var context = options.KubeContext ?? $"kind-{options.ClusterName}";

        try
        {
            var versionStep = await RunStepAsync("kind version", () => Run(options.KindBinary, ["version"], ct), progress);
            steps.Add(versionStep);
            if (!versionStep.Ok)
            {
                return Build(steps, false, options.ClusterName, false);
            }

            if (!options.ReuseExistingCluster)
            {
                var createStep = await RunStepAsync($"kind create cluster ({options.ClusterName})", () =>
                    Run(options.KindBinary, ["create", "cluster", "--name", options.ClusterName, "--wait", "60s"],
                        ct, TimeSpan.FromMinutes(15)), progress);
                steps.Add(createStep);
                if (!createStep.Ok)
                {
                    return Build(steps, false, options.ClusterName, false);
                }
                clusterCreated = true;
            }

            var loadStep = await RunStepAsync("kind load docker-image", () =>
                Run(options.KindBinary, ["load", "docker-image", options.ImageReference, "--name", options.ClusterName],
                    ct, TimeSpan.FromMinutes(5)), progress);
            steps.Add(loadStep);
            if (!loadStep.Ok)
            {
                return Build(steps, false, options.ClusterName, clusterCreated || options.ReuseExistingCluster);
            }

            var applyStep = await RunStepAsync("kubectl apply", () =>
                Run(options.KubectlBinary,
                    ["apply", "-f", options.ManifestsDirectory, "--context", context, "-n", options.Namespace],
                    ct, TimeSpan.FromMinutes(2)), progress);
            steps.Add(applyStep);
            if (!applyStep.Ok)
            {
                return Build(steps, false, options.ClusterName, clusterCreated || options.ReuseExistingCluster);
            }

            var waitStep = await RunStepAsync("kubectl wait deployment ready", () =>
                Run(options.KubectlBinary,
                    [
                        "wait",
                        "--for=condition=available",
                        "--timeout", $"{(int)options.ReadyTimeout.TotalSeconds}s",
                        "deployment",
                        options.DeploymentName,
                        "--context", context,
                        "-n", options.Namespace
                    ],
                    ct, options.ReadyTimeout + TimeSpan.FromSeconds(15)), progress);
            steps.Add(waitStep);
            if (!waitStep.Ok)
            {
                return Build(steps, false, options.ClusterName, clusterCreated || options.ReuseExistingCluster);
            }

            if (options.HttpProbePath is not null && options.HttpProbePort is { } port)
            {
                var probeStep = await ProbeHttpAsync(options, context, port, ct, progress);
                steps.Add(probeStep);
                if (!probeStep.Ok)
                {
                    return Build(steps, false, options.ClusterName, clusterCreated || options.ReuseExistingCluster);
                }
            }

            success = true;
            return Build(steps, true, options.ClusterName, clusterCreated || options.ReuseExistingCluster);
        }
        finally
        {
            if (clusterCreated && options.DeleteClusterOnComplete && !success)
            {
                await Run(options.KindBinary, ["delete", "cluster", "--name", options.ClusterName], ct, TimeSpan.FromMinutes(2));
            }
            else if (clusterCreated && options.DeleteClusterOnComplete && success)
            {
                var deleteStep = await RunStepAsync($"kind delete cluster ({options.ClusterName})", () =>
                    Run(options.KindBinary, ["delete", "cluster", "--name", options.ClusterName], ct, TimeSpan.FromMinutes(2)), progress);
                steps.Add(deleteStep);
            }
        }
    }

    private static async Task<ValidationStep> ProbeHttpAsync(
        ValidationOptions options,
        string context,
        int port,
        CancellationToken ct,
        IProgress<string>? progress)
    {
        progress?.Report($"http probe {options.HttpProbePath}:{port}");
        var sw = Stopwatch.StartNew();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = options.KubectlBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            "port-forward",
            $"deployment/{options.DeploymentName}",
            $"{port}:{port}",
            "--context", context,
            "-n", options.Namespace
        })
        {
            psi.ArgumentList.Add(arg);
        }

        System.Diagnostics.Process? proc = null;
        try
        {
            proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start kubectl port-forward");

            for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromSeconds(15); elapsed += TimeSpan.FromMilliseconds(500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                if (proc.HasExited)
                {
                    var stderr = await proc.StandardError.ReadToEndAsync(ct);
                    throw new InvalidOperationException(
                        $"kubectl port-forward exited early ({proc.ExitCode}): {stderr.TrimEnd()}");
                }
                try
                {
                    using var probe = new System.Net.Sockets.TcpClient();
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectCts.CancelAfter(TimeSpan.FromMilliseconds(300));
                    await probe.ConnectAsync(System.Net.IPAddress.Loopback, port, connectCts.Token);
                    break;
                }
                catch
                {
                }
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}{options.HttpProbePath}"), ct);
            sw.Stop();
            return new ValidationStep
            {
                Name = $"http probe {options.HttpProbePath}",
                Ok = (int)response.StatusCode is >= 200 and < 400,
                Duration = sw.Elapsed,
                Output = $"status: {(int)response.StatusCode}",
                Error = (int)response.StatusCode is >= 400 ? $"http {(int)response.StatusCode}" : null
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ValidationStep
            {
                Name = $"http probe {options.HttpProbePath}",
                Ok = false,
                Duration = sw.Elapsed,
                Error = ex.Message
            };
        }
        finally
        {
            if (proc is not null)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    await proc.WaitForExitAsync(CancellationToken.None);
                }
                catch
                {
                }
                proc.Dispose();
            }
        }
    }

    private static async Task<ValidationStep> RunStepAsync(
        string name,
        Func<Task<ProcessOutcome>> action,
        IProgress<string>? progress)
    {
        progress?.Report(name);
        var sw = Stopwatch.StartNew();
        var outcome = await action();
        sw.Stop();

        return new ValidationStep
        {
            Name = name,
            Ok = outcome.Ok,
            Duration = sw.Elapsed,
            Output = outcome.StandardOutput,
            Error = outcome.Ok ? null : (string.IsNullOrWhiteSpace(outcome.StandardError) ? $"exit {outcome.ExitCode}" : outcome.StandardError)
        };
    }

    private Task<ProcessOutcome> Run(string fileName, IReadOnlyList<string> args, CancellationToken ct, TimeSpan? timeout = null)
    {
        return runner.RunAsync(new ProcessInvocation
        {
            FileName = fileName,
            Arguments = args,
            Timeout = timeout ?? TimeSpan.FromMinutes(2)
        }, ct);
    }

    private static ValidationResult Build(IReadOnlyList<ValidationStep> steps, bool ok, string cluster, bool stillRunning) => new()
    {
        Ok = ok,
        Steps = steps,
        ClusterName = cluster,
        ClusterStillRunning = stillRunning
    };
}
