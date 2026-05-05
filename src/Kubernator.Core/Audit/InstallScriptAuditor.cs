using System.Text.RegularExpressions;

namespace Kubernator.Core.Audit;

public sealed class InstallScriptAuditor
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "DI-resolved auditor; instance-bound for testability and future state.")]
    public ManifestAuditResult AuditFile(string scriptPath)
    {
        var findings = new List<AuditFinding>();
        var inspected = new List<string> { scriptPath };

        if (!File.Exists(scriptPath))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Code = "AUD500",
                Message = $"install script not found at {scriptPath} — skipping audit"
            });
            return new ManifestAuditResult { Pass = true, Findings = findings, InspectedFiles = inspected };
        }

        string content;
        try
        {
            content = File.ReadAllText(scriptPath);
        }
        catch (Exception ex)
        {
            findings.Add(new AuditFinding { Severity = AuditSeverity.Critical, Code = "AUD501", Message = $"could not read script: {ex.Message}", FilePath = scriptPath });
            return new ManifestAuditResult { Pass = false, Findings = findings, InspectedFiles = inspected };
        }

        if (Regex.IsMatch(content, @"\bcurl\s[^\n]*\|\s*(sudo\s+)?(bash|sh)\b") ||
            Regex.IsMatch(content, @"\bwget\s[^\n]*\|\s*(sudo\s+)?(bash|sh)\b"))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Critical,
                Code = "AUD510",
                Message = "install script pipes a network download into a shell — defeats air-gapped + integrity guarantees",
                FilePath = scriptPath,
                FixHint = "load images from the bundle's images/ directory; never fetch over the network"
            });
        }

        if (Regex.IsMatch(content, @"^\s*sudo\s", RegexOptions.Multiline))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Code = "AUD511",
                Message = "install script uses sudo — review what is escalated and consider documenting it",
                FilePath = scriptPath
            });
        }

        if (Regex.IsMatch(content, @"\bchmod\s+(0?777|a\+rwx)\b"))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Code = "AUD512",
                Message = "install script sets world-writable permissions",
                FilePath = scriptPath,
                FixHint = "use 0644 / 0600 instead of 0777"
            });
        }

        if (Regex.IsMatch(content, @"\brm\s+-rf\s+/[^\s/]"))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Critical,
                Code = "AUD513",
                Message = "install script contains `rm -rf` against an absolute path near root",
                FilePath = scriptPath,
                FixHint = "scope deletions to the bundle's working directory"
            });
        }

        if (!content.StartsWith("#!", StringComparison.Ordinal))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Code = "AUD514",
                Message = "install script has no shebang",
                FilePath = scriptPath,
                FixHint = "start with `#!/usr/bin/env bash` and `set -euo pipefail`"
            });
        }
        else if (!content.Contains("set -e", StringComparison.Ordinal))
        {
            findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Code = "AUD515",
                Message = "install script does not enable `set -e` — failures may be silently ignored",
                FilePath = scriptPath
            });
        }

        return new ManifestAuditResult
        {
            Pass = !findings.Any(f => f.Severity == AuditSeverity.Critical),
            Findings = findings,
            InspectedFiles = inspected
        };
    }
}
