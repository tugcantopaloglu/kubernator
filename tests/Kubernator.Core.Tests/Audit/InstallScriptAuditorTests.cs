using Kubernator.Core.Audit;

namespace Kubernator.Core.Tests.Audit;

public sealed class InstallScriptAuditorTests : IDisposable
{
    private readonly string tempFile;
    private readonly InstallScriptAuditor sut = new();

    public InstallScriptAuditorTests()
    {
        tempFile = Path.Combine(Path.GetTempPath(), $"installtest-{Guid.NewGuid():N}.sh");
    }

    public void Dispose()
    {
        try { File.Delete(tempFile); } catch { }
    }

    [Fact]
    public void Curl_pipe_bash_is_critical()
    {
        File.WriteAllText(tempFile, "#!/usr/bin/env bash\nset -euo pipefail\ncurl https://example.com/install.sh | bash\n");
        var r = sut.AuditFile(tempFile);
        r.Pass.Should().BeFalse();
        r.Findings.Should().Contain(f => f.Code == "AUD510");
    }

    [Fact]
    public void World_writable_chmod_is_warning_but_passes()
    {
        File.WriteAllText(tempFile, "#!/usr/bin/env bash\nset -euo pipefail\nchmod 0777 /opt/app\n");
        var r = sut.AuditFile(tempFile);
        r.Pass.Should().BeTrue();
        r.Findings.Should().Contain(f => f.Code == "AUD512" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Missing_shebang_is_warning()
    {
        File.WriteAllText(tempFile, "echo hello\n");
        var r = sut.AuditFile(tempFile);
        r.Findings.Should().Contain(f => f.Code == "AUD514");
    }

    [Fact]
    public void Rm_rf_root_is_critical()
    {
        File.WriteAllText(tempFile, "#!/usr/bin/env bash\nset -euo pipefail\nrm -rf /etc/passwd\n");
        var r = sut.AuditFile(tempFile);
        r.Pass.Should().BeFalse();
        r.Findings.Should().Contain(f => f.Code == "AUD513");
    }

    [Fact]
    public void Missing_file_is_skipped_with_warning()
    {
        var r = sut.AuditFile(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sh"));
        r.Pass.Should().BeTrue();
        r.Findings.Should().Contain(f => f.Code == "AUD500");
    }
}
