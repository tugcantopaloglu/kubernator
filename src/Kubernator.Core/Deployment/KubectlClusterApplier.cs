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

    public async Task<ClusterRegistrationResult> RegisterContextAsync(ClusterRegistration registration, string kubectlBinary = "kubectl", CancellationToken ct = default)
    {
        var errors = new List<string>();
        var steps = new List<string>();

        if (string.IsNullOrWhiteSpace(registration.Name) || !System.Text.RegularExpressions.Regex.IsMatch(registration.Name, "^[A-Za-z0-9._-]+$"))
        {
            errors.Add("name must be non-empty and only contain letters, digits, dot, dash, underscore");
        }
        if (string.IsNullOrWhiteSpace(registration.ServerUrl) || !Uri.TryCreate(registration.ServerUrl, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != "https" && serverUri.Scheme != "http"))
        {
            errors.Add("server url must be an absolute http(s) URL");
        }
        if (string.IsNullOrWhiteSpace(registration.Token)
            && string.IsNullOrWhiteSpace(registration.ClientCertificatePem))
        {
            errors.Add("either a bearer token or a client-certificate/key pair is required");
        }
        if (errors.Count > 0)
        {
            return new ClusterRegistrationResult { Ok = false, Errors = errors, AppliedSteps = steps };
        }

        var tmpDir = Path.Combine(Path.GetTempPath(), $"kubernator-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var clusterArgs = new List<string> { "config", "set-cluster", registration.Name, "--server=" + registration.ServerUrl };
            if (registration.SkipTlsVerify)
            {
                clusterArgs.Add("--insecure-skip-tls-verify=true");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(registration.CaCertificatePem))
                {
                    errors.Add("CA certificate (PEM) is required unless skip-tls-verify is set");
                    return new ClusterRegistrationResult { Ok = false, Errors = errors, AppliedSteps = steps };
                }
                var caPath = Path.Combine(tmpDir, "ca.crt");
                await File.WriteAllTextAsync(caPath, registration.CaCertificatePem, ct);
                clusterArgs.Add("--certificate-authority=" + caPath);
                clusterArgs.Add("--embed-certs=true");
            }
            var clusterOutcome = await runner.RunAsync(new ProcessInvocation
            {
                FileName = kubectlBinary,
                Arguments = clusterArgs,
                Timeout = TimeSpan.FromSeconds(15)
            }, ct);
            steps.Add("set-cluster: " + (clusterOutcome.Ok ? "ok" : "fail"));
            if (!clusterOutcome.Ok)
            {
                errors.Add(clusterOutcome.StandardError.Trim());
                return new ClusterRegistrationResult { Ok = false, Errors = errors, AppliedSteps = steps };
            }

            var userName = registration.Name + "-user";
            var credArgs = new List<string> { "config", "set-credentials", userName };
            if (!string.IsNullOrWhiteSpace(registration.Token))
            {
                credArgs.Add("--token=" + registration.Token);
            }
            else
            {
                var certPath = Path.Combine(tmpDir, "client.crt");
                var keyPath = Path.Combine(tmpDir, "client.key");
                await File.WriteAllTextAsync(certPath, registration.ClientCertificatePem!, ct);
                await File.WriteAllTextAsync(keyPath, registration.ClientKeyPem ?? string.Empty, ct);
                credArgs.Add("--client-certificate=" + certPath);
                credArgs.Add("--client-key=" + keyPath);
                credArgs.Add("--embed-certs=true");
            }
            var credOutcome = await runner.RunAsync(new ProcessInvocation
            {
                FileName = kubectlBinary,
                Arguments = credArgs,
                Timeout = TimeSpan.FromSeconds(15)
            }, ct);
            steps.Add("set-credentials: " + (credOutcome.Ok ? "ok" : "fail"));
            if (!credOutcome.Ok)
            {
                errors.Add(credOutcome.StandardError.Trim());
                return new ClusterRegistrationResult { Ok = false, Errors = errors, AppliedSteps = steps };
            }

            var ctxArgs = new List<string> { "config", "set-context", registration.Name, "--cluster=" + registration.Name, "--user=" + userName };
            if (!string.IsNullOrWhiteSpace(registration.Namespace))
            {
                ctxArgs.Add("--namespace=" + registration.Namespace);
            }
            var ctxOutcome = await runner.RunAsync(new ProcessInvocation
            {
                FileName = kubectlBinary,
                Arguments = ctxArgs,
                Timeout = TimeSpan.FromSeconds(15)
            }, ct);
            steps.Add("set-context: " + (ctxOutcome.Ok ? "ok" : "fail"));
            if (!ctxOutcome.Ok)
            {
                errors.Add(ctxOutcome.StandardError.Trim());
                return new ClusterRegistrationResult { Ok = false, Errors = errors, AppliedSteps = steps };
            }

            return new ClusterRegistrationResult { Ok = true, Errors = errors, AppliedSteps = steps };
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }
}
