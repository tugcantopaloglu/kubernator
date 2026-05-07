using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kubernator.Web.Api;
using Kubernator.Web.Auth;

namespace Kubernator.Web.Tests.Api;

[Collection("api-suite")]
public sealed class AdminAndScopeTests
{
    private readonly ApiTests.Factory factory;

    public AdminAndScopeTests(ApiTests.Factory factory)
    {
        this.factory = factory;
    }

    private HttpClient ClientWithKey(string key)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, key);
        return c;
    }

    [Fact]
    public async Task Bootstrap_key_can_list_keys()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.GetAsync("/api/v1/admin/api-keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bootstrap_can_create_read_key_and_use_it()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var createResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "test-read-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Read"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var plaintext = created.GetProperty("plaintextKey").GetString()!;
        plaintext.Should().StartWith("knk_");

        var readClient = ClientWithKey(plaintext);

        var detect = await readClient.PostAsJsonAsync("/api/v1/detect", new { path = Path.GetTempPath() });
        detect.StatusCode.Should().Be(HttpStatusCode.OK);

        var generate = await readClient.PostAsJsonAsync("/api/v1/generate", new { path = Path.GetTempPath() });
        generate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Generate_scope_can_call_generate_but_not_admin()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var createResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "test-gen-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Generate"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var plaintext = created.GetProperty("plaintextKey").GetString()!;
        var keyId = created.GetProperty("record").GetProperty("id").GetString()!;

        var generateClient = ClientWithKey(plaintext);

        var listKeys = await generateClient.GetAsync("/api/v1/admin/api-keys");
        listKeys.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await admin.DeleteAsync($"/api/v1/admin/api-keys/{keyId}");
    }

    [Fact]
    public async Task Disabled_key_is_rejected()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var createResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "test-disable-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Read"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var plaintext = created.GetProperty("plaintextKey").GetString()!;
        var keyId = created.GetProperty("record").GetProperty("id").GetString()!;

        var disabledResp = await admin.PatchAsJsonAsync($"/api/v1/admin/api-keys/{keyId}", new { disabled = true });
        disabledResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejected = ClientWithKey(plaintext);
        var detect = await rejected.PostAsJsonAsync("/api/v1/detect", new { path = Path.GetTempPath() });
        detect.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_key_is_rejected()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var futureBy1h = DateTimeOffset.UtcNow.AddHours(1);
        var createResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "test-exp-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Read",
            expiresAt = futureBy1h
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var inThePast = DateTimeOffset.UtcNow.AddSeconds(-5);
        var badResp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "test-past-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Read",
            expiresAt = inThePast
        });
        badResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_key_name_returns_conflict()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var name = "dup-" + Guid.NewGuid().ToString("N")[..6];
        var first = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new { name, scope = "Read" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new { name, scope = "Read" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_scope_returns_400()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var resp = await admin.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = "weird-" + Guid.NewGuid().ToString("N")[..6],
            scope = "Sudo"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Title.Should().Be("invalid scope");
    }

    [Fact]
    public async Task Delete_unknown_key_returns_404()
    {
        var admin = ClientWithKey(ApiTests.Factory.TestApiKey);
        var resp = await admin.DeleteAsync("/api/v1/admin/api-keys/deadbeefdeadbeef");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Jobs_list_returns_payload()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.GetAsync("/api/v1/jobs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("jobs").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Jobs_get_unknown_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.GetAsync("/api/v1/jobs/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Build_submission_returns_202_and_job_completes()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var sandbox = TestUtil.TempDir();
        File.WriteAllText(Path.Combine(sandbox, "index.html"), "<html></html>");
        var output = TestUtil.TempDir();

        var resp = await client.PostAsJsonAsync("/api/v1/build", new
        {
            path = sandbox,
            outputDirectory = output,
            noBuild = true
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("id").GetString()!;
        var location = accepted.GetProperty("location").GetString()!;
        location.Should().Contain(jobId);

        var done = await TestUtil.WaitForJobAsync(client, jobId, TimeSpan.FromSeconds(15));
        done.GetProperty("status").GetString().Should().Be("Succeeded");
        var result = done.GetProperty("result");
        result.GetProperty("outputDirectory").GetString().Should().Be(output);
        result.GetProperty("generatedFileCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Build_with_missing_path_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var resp = await client.PostAsJsonAsync("/api/v1/build", new { path = Path.Combine(Path.GetTempPath(), "noooope-" + Guid.NewGuid().ToString("N")) });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deploy_production_context_without_allow_returns_409()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var manifests = TestUtil.TempDir();
        var resp = await client.PostAsJsonAsync("/api/v1/deploy", new
        {
            manifestsDirectory = manifests,
            context = "prod-eu",
            dryRun = false
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Title.Should().Be("production context refused");
    }

    [Fact]
    public async Task Deploy_with_unknown_manifest_dir_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var resp = await client.PostAsJsonAsync("/api/v1/deploy", new
        {
            manifestsDirectory = Path.Combine(Path.GetTempPath(), "no-" + Guid.NewGuid().ToString("N")),
            context = "kind-test",
            dryRun = true
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Monitor_missing_context_returns_400()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var resp = await client.PostAsJsonAsync("/api/v1/monitor", new { context = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

internal static class TestUtil
{
    public static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), $"kubn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    public static async Task<JsonElement> WaitForJobAsync(HttpClient client, string jobId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var resp = await client.GetAsync($"/api/v1/jobs/{jobId}");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var status = json.GetProperty("status").GetString();
            if (status is "Succeeded" or "Failed" or "Cancelled")
            {
                return json;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"job {jobId} did not finish within {timeout}");
    }
}
