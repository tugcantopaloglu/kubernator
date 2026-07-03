using System.Security.Claims;
using System.Text.Json;
using Kubernator.Web.Logging;
using Kubernator.Web.Ui;
using Microsoft.AspNetCore.Components.Authorization;

namespace Kubernator.Web.Tests.Ui;

public sealed class UiActionGatewayTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string auditDir = Path.Combine(Path.GetTempPath(), $"kubn-ui-audit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(auditDir, recursive: true); } catch { }
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal principal;

        public StubAuthStateProvider(string? userName)
        {
            var identity = userName is null
                ? new ClaimsIdentity()
                : new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, userName),
                        new Claim("auth_method", "password+totp")
                    },
                    authenticationType: "test");
            principal = new ClaimsPrincipal(identity);
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private static UiActionGateway CreateGateway(AuditLog audit, UiActionRateLimiter limiter, string? user)
        => new(audit, limiter, new StubAuthStateProvider(user));

    private List<AuditEntry> ReadAuditEntries()
    {
        if (!Directory.Exists(auditDir)) return new();
        return Directory.EnumerateFiles(auditDir, "audit-*.jsonl")
            .SelectMany(ReadSharedLines)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<AuditEntry>(l, Json)!)
            .ToList();
    }

    // AuditLog keeps its writer open with FileShare.ReadWrite, so a reader must match that
    // share mode rather than File.ReadAllLines' default (FileShare.Read).
    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }
        return lines;
    }

    [Fact]
    public async Task InvokeAsync_writes_success_audit_entry_attributed_to_the_session_user()
    {
        using var audit = new AuditLog(auditDir);
        using var limiter = new UiActionRateLimiter(perMinute: 10);
        var gateway = CreateGateway(audit, limiter, user: "alice");

        var result = await gateway.InvokeAsync("monitor/snapshot", _ => Task.FromResult(42));

        result.Should().Be(42);
        var entries = ReadAuditEntries();
        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.Method.Should().Be("UI");
        entry.Path.Should().Be("monitor/snapshot");
        entry.StatusCode.Should().Be(200);
        entry.KeyName.Should().Be("alice");
        entry.Scope.Should().Be("session");
        entry.AuthMethod.Should().Be("password+totp");
        entry.Error.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_records_a_500_entry_and_rethrows_when_the_action_fails()
    {
        using var audit = new AuditLog(auditDir);
        using var limiter = new UiActionRateLimiter(perMinute: 10);
        var gateway = CreateGateway(audit, limiter, user: "alice");

        Func<Task> act = () => gateway.InvokeAsync<int>("deploy/apply",
            _ => throw new InvalidOperationException("kubectl blew up"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("kubectl blew up");

        var entry = ReadAuditEntries().Should().ContainSingle().Subject;
        entry.Path.Should().Be("deploy/apply");
        entry.StatusCode.Should().Be(500);
        entry.Error.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task InvokeAsync_falls_back_to_anonymous_when_no_user_is_authenticated()
    {
        using var audit = new AuditLog(auditDir);
        using var limiter = new UiActionRateLimiter(perMinute: 10);
        var gateway = CreateGateway(audit, limiter, user: null);

        await gateway.InvokeAsync("monitor/snapshot", _ => Task.FromResult(0));

        var entry = ReadAuditEntries().Should().ContainSingle().Subject;
        entry.KeyName.Should().Be("anonymous");
        entry.AuthMethod.Should().Be("session-cookie");
    }

    [Fact]
    public async Task InvokeAsync_blocks_once_the_per_minute_budget_is_exhausted()
    {
        using var audit = new AuditLog(auditDir);
        using var limiter = new UiActionRateLimiter(perMinute: 2);
        var gateway = CreateGateway(audit, limiter, user: "alice");

        await gateway.InvokeAsync("cluster/install", () => 1);
        await gateway.InvokeAsync("cluster/install", () => 1);

        Func<Task> third = () => gateway.InvokeAsync("cluster/install", () => 1);
        await third.Should().ThrowAsync<UiRateLimitException>();

        var entries = ReadAuditEntries();
        entries.Should().HaveCount(3);
        entries.Count(e => e.StatusCode == 200).Should().Be(2);
        entries.Should().ContainSingle(e => e.StatusCode == 429);
    }

    [Fact]
    public async Task Rate_limit_is_partitioned_per_user()
    {
        using var audit = new AuditLog(auditDir);
        using var limiter = new UiActionRateLimiter(perMinute: 1);
        var alice = CreateGateway(audit, limiter, user: "alice");
        var bob = CreateGateway(audit, limiter, user: "bob");

        await alice.InvokeAsync("cluster/status", () => 1);

        // alice is now out of budget…
        Func<Task> aliceSecond = () => alice.InvokeAsync("cluster/status", () => 1);
        await aliceSecond.Should().ThrowAsync<UiRateLimitException>();

        // …but bob still has his own window.
        Func<Task> bobFirst = () => bob.InvokeAsync("cluster/status", () => 1);
        await bobFirst.Should().NotThrowAsync();
    }
}
