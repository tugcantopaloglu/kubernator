using Kubernator.Core.Audit;

namespace Kubernator.Core.Tests.Audit;

public sealed class ManifestAuditorTests : IDisposable
{
    private readonly string tempDir;
    private readonly ManifestAuditor sut = new();

    public ManifestAuditorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(tempDir, name), content);

    [Fact]
    public void Empty_directory_fails_with_AUD000()
    {
        var r = sut.AuditDirectory(tempDir);
        r.Pass.Should().BeFalse();
        r.Findings.Should().Contain(f => f.Code == "AUD000");
    }

    [Fact]
    public void Hardened_manifest_passes_baseline()
    {
        Write("deployment.yaml", HardenedDeployment);
        Write("networkpolicy.yaml", DefaultDenyNetworkPolicy);

        var r = sut.AuditDirectory(tempDir, "myns");
        r.Pass.Should().BeTrue();
        r.Findings.Where(f => f.Severity == AuditSeverity.Critical).Should().BeEmpty();
    }

    [Fact]
    public void Privileged_container_triggers_AUD112_and_AUD113()
    {
        Write("deployment.yaml", BadDeployment);
        var r = sut.AuditDirectory(tempDir);
        r.Pass.Should().BeFalse();
        r.Findings.Should().Contain(f => f.Code == "AUD112");
        r.Findings.Should().Contain(f => f.Code == "AUD113");
    }

    [Fact]
    public void Missing_NetworkPolicy_triggers_AUD200()
    {
        Write("deployment.yaml", HardenedDeployment);
        var r = sut.AuditDirectory(tempDir);
        r.Findings.Should().Contain(f => f.Code == "AUD200" && f.Severity == AuditSeverity.Critical);
    }

    [Fact]
    public void Cross_namespace_triggers_AUD401_critical()
    {
        Write("deployment.yaml", HardenedDeployment.Replace("namespace: myns", "namespace: other"));
        Write("networkpolicy.yaml", DefaultDenyNetworkPolicy.Replace("namespace: myns", "namespace: other"));
        var r = sut.AuditDirectory(tempDir, "myns");
        r.Pass.Should().BeFalse();
        r.Findings.Should().Contain(f => f.Code == "AUD401");
    }

    [Fact]
    public void Latest_image_triggers_AUD301_warning()
    {
        Write("deployment.yaml", HardenedDeployment.Replace("image: \"app:1.0\"", "image: \"app:latest\""));
        Write("networkpolicy.yaml", DefaultDenyNetworkPolicy);
        var r = sut.AuditDirectory(tempDir, "myns");
        r.Findings.Should().Contain(f => f.Code == "AUD301" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Findings_carry_fix_hints()
    {
        Write("deployment.yaml", BadDeployment);
        var r = sut.AuditDirectory(tempDir);
        r.Findings.Where(f => f.Code == "AUD112").Single().FixHint.Should().NotBeNullOrWhiteSpace();
    }

    private const string HardenedDeployment = """
apiVersion: apps/v1
kind: Deployment
metadata:
  name: app
  namespace: myns
spec:
  template:
    spec:
      automountServiceAccountToken: false
      securityContext:
        runAsNonRoot: true
        runAsUser: 1654
        seccompProfile:
          type: RuntimeDefault
      containers:
        - name: app
          image: "app:1.0"
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities:
              drop:
                - ALL
          resources:
            requests:
              cpu: 100m
              memory: 64Mi
            limits:
              cpu: 500m
              memory: 256Mi
""";

    private const string DefaultDenyNetworkPolicy = """
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: app-default-deny
  namespace: myns
spec:
  podSelector: {}
  policyTypes:
    - Ingress
    - Egress
  egress:
    - to:
        - namespaceSelector:
            matchLabels:
              kubernetes.io/metadata.name: kube-system
""";

    private const string BadDeployment = """
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bad
spec:
  template:
    spec:
      hostNetwork: true
      containers:
        - name: c
          image: busybox:latest
          securityContext:
            privileged: true
            runAsUser: 0
""";
}
