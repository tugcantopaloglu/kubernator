using Kubernator.Core.Validation;

namespace Kubernator.Core.Deployment;

public sealed class KubectlClusterApplier : IClusterApplier
{
    private readonly IProcessRunner runner;

    public KubectlClusterApplier(IProcessRunner runner)
    {
        this.runner = runner;
    }

    public async Task<IReadOnlyList<ClusterContext>> ListContextsAsync(string kubectlBinary = "kubectl", CancellationToken ct = default)
    {
        var contextsOutcome = await runner.RunAsync(new ProcessInvocation
        {
            FileName = kubectlBinary,
            Arguments = ["config", "get-contexts", "-o", "name"],
            Timeout = TimeSpan.FromSeconds(10)
        }, ct);

        if (!contextsOutcome.Ok)
        {
            return [];
        }

        var current = string.Empty;
        var currentOutcome = await runner.RunAsync(new ProcessInvocation
        {
            FileName = kubectlBinary,
            Arguments = ["config", "current-context"],
            Timeout = TimeSpan.FromSeconds(5)
        }, ct);
        if (currentOutcome.Ok)
        {
            current = currentOutcome.StandardOutput.Trim();
        }

        var names = contextsOutcome.StandardOutput
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var list = new List<ClusterContext>(names.Length);
        foreach (var name in names)
        {
            list.Add(new ClusterContext
            {
                Name = name,
                IsCurrent = string.Equals(name, current, StringComparison.Ordinal)
            });
        }
        return list;
    }

    public async Task<DeployResult> ApplyAsync(DeployOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(options.ManifestsDirectory))
        {
            return new DeployResult
            {
                Ok = false,
                Context = options.Context,
                Namespace = options.Namespace,
                DryRun = options.DryRun,
                AppliedResources = [],
                Errors = [$"manifests directory not found: {options.ManifestsDirectory}"]
            };
        }

        if (ClusterContext.LooksLikeProduction(options.Context) && !options.AllowProduction)
        {
            return new DeployResult
            {
                Ok = false,
                Context = options.Context,
                Namespace = options.Namespace,
                DryRun = options.DryRun,
                AppliedResources = [],
                Errors = [$"context '{options.Context}' looks like a production cluster — pass AllowProduction=true (CLI: --allow-production) to proceed"]
            };
        }

        if (options.CreateNamespace)
        {
            progress?.Report($"ensuring namespace {options.Namespace}");
            var nsArgs = new List<string>
            {
                "--context", options.Context,
                "create", "namespace", options.Namespace
            };
            if (options.DryRun)
            {
                nsArgs.Add("--dry-run=client");
            }
            var nsOutcome = await runner.RunAsync(new ProcessInvocation
            {
                FileName = options.KubectlBinary,
                Arguments = nsArgs,
                Timeout = TimeSpan.FromSeconds(15)
            }, ct);
            var alreadyExists = !nsOutcome.Ok
                && (nsOutcome.StandardError.Contains("AlreadyExists", StringComparison.Ordinal)
                    || nsOutcome.StandardError.Contains("already exists", StringComparison.Ordinal));
            if (!nsOutcome.Ok && !alreadyExists)
            {
                progress?.Report(nsOutcome.StandardError.Trim());
            }
        }

        progress?.Report(options.DryRun
            ? $"kubectl apply --dry-run=server (context {options.Context}, namespace {options.Namespace})"
            : $"kubectl apply (context {options.Context}, namespace {options.Namespace})");

        var args = new List<string>
        {
            "--context", options.Context,
            "-n", options.Namespace,
            "apply",
            "-f", options.ManifestsDirectory
        };
        if (options.DryRun)
        {
            args.Add("--dry-run=server");
        }

        var outcome = await runner.RunAsync(new ProcessInvocation
        {
            FileName = options.KubectlBinary,
            Arguments = args,
            Timeout = options.Timeout
        }, ct);

        var lines = outcome.StandardOutput
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            progress?.Report(line);
        }
        var stderrLines = outcome.StandardError
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in stderrLines)
        {
            progress?.Report(line);
        }

        var errors = outcome.Ok
            ? new List<string>()
            : new List<string>(stderrLines.Length > 0 ? stderrLines : ["kubectl exited " + outcome.ExitCode]);

        return new DeployResult
        {
            Ok = outcome.Ok,
            Context = options.Context,
            Namespace = options.Namespace,
            DryRun = options.DryRun,
            AppliedResources = lines,
            Errors = errors
        };
    }
}
