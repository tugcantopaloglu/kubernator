using System.Text.RegularExpressions;

namespace Kubernator.Core.Audit;

public enum AuditSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record AuditFinding
{
    public required AuditSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public string? FixHint { get; init; }
}

public sealed record ManifestAuditResult
{
    public required bool Pass { get; init; }
    public required IReadOnlyList<AuditFinding> Findings { get; init; }
    public required IReadOnlyList<string> InspectedFiles { get; init; }

    public bool HasCritical => Findings.Any(f => f.Severity == AuditSeverity.Critical);
}

public sealed class ManifestAuditor
{
    private static readonly string[] AllowedRegistryPrefixes =
    [
        "mcr.microsoft.com/",
        "cgr.dev/chainguard/",
        "gcr.io/distroless/",
        "registry.k8s.io/"
    ];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "DI-resolved auditor; instance-bound for testability and future state.")]
    public ManifestAuditResult AuditDirectory(string directory, string? expectedNamespace = null)
    {
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.y*ml", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList()
            : new List<string>();

        if (files.Count == 0)
        {
            return new ManifestAuditResult
            {
                Pass = false,
                Findings = [Critical("AUD000", $"no manifests found at {directory}")],
                InspectedFiles = files
            };
        }

        var findings = new List<AuditFinding>();
        string? combined = null;
        foreach (var file in files)
        {
            try
            {
                combined = (combined ?? string.Empty) + "\n---\n" + File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                findings.Add(Critical("AUD001", $"could not read {Path.GetFileName(file)}: {ex.Message}", file));
            }
        }

        if (combined is null)
        {
            return new ManifestAuditResult { Pass = false, Findings = findings, InspectedFiles = files };
        }

        AuditDeployment(combined, findings);
        AuditNetworkPolicy(combined, findings);
        AuditImages(combined, findings);
        if (!string.IsNullOrEmpty(expectedNamespace))
        {
            AuditNamespace(combined, expectedNamespace, findings);
        }

        return new ManifestAuditResult
        {
            Pass = !findings.Any(f => f.Severity == AuditSeverity.Critical),
            Findings = findings,
            InspectedFiles = files
        };
    }

    private static void AuditDeployment(string yaml, List<AuditFinding> findings)
    {
        if (!Contains(yaml, "kind: Deployment"))
        {
            findings.Add(Critical("AUD100", "no Deployment found in the manifest set"));
            return;
        }

        Require(yaml, "runAsNonRoot: true", "AUD101", "pod-level securityContext is missing 'runAsNonRoot: true'", findings);
        Require(yaml, "readOnlyRootFilesystem: true", "AUD102", "container securityContext is missing 'readOnlyRootFilesystem: true'", findings);
        Require(yaml, "allowPrivilegeEscalation: false", "AUD103", "container securityContext is missing 'allowPrivilegeEscalation: false'", findings);
        Require(yaml, "- ALL", "AUD104", "container does not drop ALL Linux capabilities", findings);
        Require(yaml, "automountServiceAccountToken: false", "AUD105", "pod still mounts the default service-account token", findings);
        Require(yaml, "type: RuntimeDefault", "AUD106", "pod is missing seccompProfile RuntimeDefault", findings);

        if (Regex.IsMatch(yaml, @"^\s*hostNetwork:\s*true", RegexOptions.Multiline))
        {
            findings.Add(Critical("AUD110", "hostNetwork=true grants the pod the node's network namespace"));
        }
        if (Regex.IsMatch(yaml, @"^\s*hostPID:\s*true", RegexOptions.Multiline))
        {
            findings.Add(Critical("AUD111", "hostPID=true exposes other processes on the node"));
        }
        if (Regex.IsMatch(yaml, @"^\s*privileged:\s*true", RegexOptions.Multiline))
        {
            findings.Add(Critical("AUD112", "container is configured 'privileged: true' — this defeats the security model"));
        }
        if (Regex.IsMatch(yaml, @"^\s*runAsUser:\s*0\b", RegexOptions.Multiline))
        {
            findings.Add(Critical("AUD113", "runAsUser=0 — must run as a non-zero UID"));
        }

        if (!Regex.IsMatch(yaml, @"^\s*resources:\s*$", RegexOptions.Multiline)
            || !Contains(yaml, "limits:"))
        {
            findings.Add(Warn("AUD120", "container resource requests/limits not detected — workload could starve neighbours"));
        }
    }

    private static void AuditNetworkPolicy(string yaml, List<AuditFinding> findings)
    {
        if (!Contains(yaml, "kind: NetworkPolicy"))
        {
            findings.Add(Critical("AUD200", "no NetworkPolicy — pod could reach every other workload in the cluster"));
            return;
        }
        if (!Contains(yaml, "policyTypes:") || (!Contains(yaml, "- Ingress") || !Contains(yaml, "- Egress")))
        {
            findings.Add(Critical("AUD201", "NetworkPolicy does not declare both Ingress and Egress policyTypes"));
        }
        if (!Contains(yaml, "kubernetes.io/metadata.name: kube-system"))
        {
            findings.Add(Warn("AUD202", "NetworkPolicy egress whitelist does not appear to limit DNS to kube-system"));
        }
    }

    private static void AuditImages(string yaml, List<AuditFinding> findings)
    {
        var matches = Regex.Matches(yaml, @"^\s*image:\s*""?([^""\s]+)""?\s*$", RegexOptions.Multiline);
        if (matches.Count == 0)
        {
            findings.Add(Warn("AUD300", "no 'image:' references discovered in the manifest"));
            return;
        }
        foreach (Match m in matches)
        {
            var image = m.Groups[1].Value;
            if (image.Contains(":latest", StringComparison.Ordinal) || (!image.Contains(':', StringComparison.Ordinal) && !image.Contains('@', StringComparison.Ordinal)))
            {
                findings.Add(Warn("AUD301", $"image '{image}' uses ':latest' or no tag — pin to a digest or version"));
            }
        }
    }

    private static void AuditNamespace(string yaml, string expectedNamespace, List<AuditFinding> findings)
    {
        var declared = Regex.Matches(yaml, @"^\s*namespace:\s*([\w-]+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (declared.Count == 0)
        {
            findings.Add(Warn("AUD400", $"no namespace declared in manifests; deploy will land in the kubectl context default rather than '{expectedNamespace}'"));
            return;
        }
        var foreign = declared.Where(n => !string.Equals(n, expectedNamespace, StringComparison.Ordinal)).ToList();
        if (foreign.Count > 0)
        {
            findings.Add(Critical("AUD401", $"manifest declares namespace(s) [{string.Join(", ", foreign)}] but deploy target is '{expectedNamespace}' — refuse to cross namespaces unless intentional"));
        }
    }

    private static bool Contains(string yaml, string needle) => yaml.Contains(needle, StringComparison.Ordinal);

    private static void Require(string yaml, string needle, string code, string message, List<AuditFinding> findings)
    {
        if (!Contains(yaml, needle))
        {
            findings.Add(Critical(code, message));
        }
    }

    private static AuditFinding Critical(string code, string message, string? file = null) => new()
    {
        Severity = AuditSeverity.Critical,
        Code = code,
        Message = message,
        FilePath = file,
        FixHint = FixHints.GetValueOrDefault(code)
    };

    private static AuditFinding Warn(string code, string message, string? file = null) => new()
    {
        Severity = AuditSeverity.Warning,
        Code = code,
        Message = message,
        FilePath = file,
        FixHint = FixHints.GetValueOrDefault(code)
    };

    private static readonly IReadOnlyDictionary<string, string> FixHints = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AUD101"] = "add `runAsNonRoot: true` under spec.template.spec.securityContext",
        ["AUD102"] = "add `readOnlyRootFilesystem: true` under each container's securityContext",
        ["AUD103"] = "add `allowPrivilegeEscalation: false` under each container's securityContext",
        ["AUD104"] = "add `capabilities: { drop: [ALL] }` under each container's securityContext",
        ["AUD105"] = "add `automountServiceAccountToken: false` to spec.template.spec",
        ["AUD106"] = "add `seccompProfile: { type: RuntimeDefault }` under spec.template.spec.securityContext",
        ["AUD110"] = "remove `hostNetwork: true`",
        ["AUD111"] = "remove `hostPID: true`",
        ["AUD112"] = "remove `privileged: true` from container.securityContext",
        ["AUD113"] = "set `runAsUser` to a non-zero UID such as 1654 or 65532",
        ["AUD120"] = "add `resources: { requests: {cpu, memory}, limits: {cpu, memory} }` to each container",
        ["AUD200"] = "add a NetworkPolicy with default-deny ingress + egress (kubernator's generator does this for you)",
        ["AUD201"] = "set `policyTypes: [Ingress, Egress]` on the NetworkPolicy",
        ["AUD301"] = "pin the image to an immutable digest: `image@sha256:...`",
        ["AUD401"] = "set `metadata.namespace` to match the deploy target on every resource"
    };
}
