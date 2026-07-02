using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kubernator.Web.Api;

namespace Kubernator.Web.Tests.Api;

[Collection("api-suite")]
public sealed class RateLimitAndAuditTests
{
    private readonly ApiTests.Factory factory;

    public RateLimitAndAuditTests(ApiTests.Factory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_problem_details()
    {
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, ApiTests.Factory.TestApiKey);

        var name = "ratelimit-" + Guid.NewGuid().ToString("N")[..6];
        var createResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name,
            scope = "Read",
            rateLimitPerMinute = 3
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var plaintext = created.GetProperty("plaintextKey").GetString()!;

        var probe = factory.CreateClient();
        probe.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, plaintext);

        var scanDir = TestUtil.TempDir();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 8; i++)
        {
            var resp = await probe.PostAsJsonAsync("/api/v1/detect", new { path = scanDir });
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = resp;
                break;
            }
        }
        rejected.Should().NotBeNull("rate limit must trip within 8 requests when limit is 3/min");
        rejected!.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await rejected.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Status.Should().Be(429);
        problem.Title.Should().Contain("rate limit");
    }

    [Fact]
    public async Task Audit_log_records_request()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, ApiTests.Factory.TestApiKey);

        var probeId = "audit-probe-" + Guid.NewGuid().ToString("N")[..8];
        await client.GetAsync($"/api/v1/version?marker={probeId}");

        var auditDir = Path.Combine(factory.Home, "audit");
        Directory.Exists(auditDir).Should().BeTrue();
        var files = Directory.GetFiles(auditDir, "audit-*.jsonl");
        files.Should().NotBeEmpty();

        var lines = files.SelectMany(ReadAllLinesShared).ToArray();
        lines.Should().Contain(l => l.Contains("/api/v1/version", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var list = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            list.Add(line);
        }
        return list;
    }
}
