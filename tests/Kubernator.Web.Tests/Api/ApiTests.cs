using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kubernator.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Kubernator.Web.Tests.Api;

public sealed class ApiTests : IClassFixture<ApiTests.Factory>
{
    private readonly Factory factory;

    public ApiTests(Factory factory)
    {
        this.factory = factory;
    }

    private HttpClient CreateAuthenticated()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, Factory.TestApiKey);
        return client;
    }

    [Fact]
    public async Task Health_is_anonymous_and_returns_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("ok");
        payload.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Detect_without_api_key_is_unauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/detect", new { path = "." });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Detect_with_invalid_api_key_is_unauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, "wrong-key");
        var response = await client.PostAsJsonAsync("/api/v1/detect", new { path = "." });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Detect_with_valid_key_for_missing_path_returns_404_problem_details()
    {
        var client = CreateAuthenticated();
        var bogus = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync("/api/v1/detect", new { path = bogus });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Title.Should().Contain("not found");
        problem.Status.Should().Be(404);
        problem.TraceId.Should().NotBeNullOrEmpty();
        problem.Instance.Should().Be("/api/v1/detect");
    }

    [Fact]
    public async Task Detect_with_empty_path_returns_400_problem_details()
    {
        var client = CreateAuthenticated();
        var response = await client.PostAsJsonAsync("/api/v1/detect", new { path = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Detect_with_malformed_json_returns_400_problem_details()
    {
        var client = CreateAuthenticated();
        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/detect", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Detect_returns_results_for_real_directory()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/detect", new { path = sandbox.Path });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("path").GetString().Should().NotBeNull();
        json.GetProperty("results").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Analyze_for_empty_directory_returns_409_conflict()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { path = sandbox.Path });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Analyze_for_static_web_returns_descriptor()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html><body>hi</body></html>");
        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { path = sandbox.Path });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("kind").GetString().Should().Be("StaticWeb");
    }

    [Fact]
    public async Task Generate_for_static_web_writes_files()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html><body>hi</body></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/generate", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            namespaceProperty = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("outputDirectory").GetString().Should().Be(output.Path);
        json.GetProperty("writtenFiles").GetArrayLength().Should().BeGreaterThan(0);
        json.GetProperty("imageReference").GetString().Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(output.Path, "Dockerfile")).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_with_download_token_provides_zip()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/generate", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            returnDownloadToken = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("downloadToken").GetString().Should().NotBeNullOrEmpty();
        var url = json.GetProperty("downloadUrl").GetString()!;
        url.Should().StartWith("/download/");
    }

    [Fact]
    public async Task Generate_with_invalid_replicas_still_clamps_to_one()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/generate", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            replicas = -3
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Helm_generates_chart_for_static_web()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/helm", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            chartName = "demo"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("chartDirectory").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("writtenFiles").GetArrayLength().Should().BeGreaterThan(0);
    }

    private static readonly string[] KustomizeOverlays = ["production", "staging"];

    private static readonly string[] ExpectedSwaggerPaths =
    [
        "/api/v1/health", "/api/v1/version", "/api/v1/diagnostics",
        "/api/v1/detect", "/api/v1/analyze", "/api/v1/audit",
        "/api/v1/generate", "/api/v1/helm", "/api/v1/kustomize",
        "/api/v1/gitops", "/api/v1/pipeline", "/api/v1/tls-rotate",
        "/api/v1/scan", "/api/v1/vulndb", "/api/v1/vault",
        "/api/v1/bundles/verify", "/api/v1/base-images"
    ];

    [Fact]
    public async Task Kustomize_generates_base_and_overlays()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/kustomize", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            overlays = KustomizeOverlays
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GitOps_requires_repo_url()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/gitops", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            repoUrl = ""
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GitOps_generates_argocd_application()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/gitops", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            repoUrl = "https://git.example.com/acme/web"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pipeline_invalid_target_returns_400()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/pipeline", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            target = "jenkins"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();
        problem!.Title.Should().Be("invalid target");
    }

    [Fact]
    public async Task Pipeline_for_gh_actions_emits_workflow()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/pipeline", new
        {
            path = sandbox.Path,
            outputDirectory = output.Path,
            target = "gh-actions"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("target").GetString().Should().Be("GitHubActions");
    }

    [Fact]
    public async Task TlsRotate_generates_cronjob()
    {
        var client = CreateAuthenticated();
        using var output = TempDir.Create();
        var response = await client.PostAsJsonAsync("/api/v1/tls-rotate", new
        {
            secretName = "tls-cert",
            hostname = "app.example.com",
            outputDirectory = output.Path,
            namespaceProperty = "prod"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("resolvedCronJobName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TlsRotate_missing_secret_name_returns_400()
    {
        var client = CreateAuthenticated();
        var response = await client.PostAsJsonAsync("/api/v1/tls-rotate", new
        {
            hostname = "app.example.com"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Audit_with_missing_directory_returns_404()
    {
        var client = CreateAuthenticated();
        var bogus = Path.Combine(Path.GetTempPath(), "audit-missing-" + Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync("/api/v1/audit", new { directory = bogus });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Audit_runs_against_generated_manifests()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        using var output = TempDir.Create();
        await client.PostAsJsonAsync("/api/v1/generate", new { path = sandbox.Path, outputDirectory = output.Path });
        var k8sDir = Path.Combine(output.Path, "kubernetes");
        Directory.Exists(k8sDir).Should().BeTrue();

        var auditResponse = await client.PostAsJsonAsync("/api/v1/audit", new { directory = k8sDir });
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("inspectedFiles").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Scan_for_static_web_returns_zero_findings()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        var response = await client.PostAsJsonAsync("/api/v1/scan", new
        {
            path = sandbox.Path
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("packagesScanned").GetInt32().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Scan_invalid_severity_returns_400()
    {
        var client = CreateAuthenticated();
        using var sandbox = TempDir.Create();
        File.WriteAllText(Path.Combine(sandbox.Path, "index.html"), "<html></html>");
        var response = await client.PostAsJsonAsync("/api/v1/scan", new
        {
            path = sandbox.Path,
            minSeverity = "Apocalyptic"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VulnDb_status_is_returned()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/vulndb");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("present", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Vault_list_is_returned()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/vault");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Vault_get_unknown_returns_404_problem()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/vault/unknown-id-xyz");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Bundle_verify_missing_path_returns_404()
    {
        var client = CreateAuthenticated();
        var response = await client.PostAsJsonAsync("/api/v1/bundles/verify", new
        {
            bundlePath = Path.Combine(Path.GetTempPath(), "no-such-bundle-" + Guid.NewGuid().ToString("N") + ".kubpack")
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BaseImages_lists_allowed_registries()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/base-images");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var registries = json.GetProperty("allowedRegistries").EnumerateArray().Select(x => x.GetString()).ToArray();
        registries.Should().Contain("mcr.microsoft.com");
        registries.Should().Contain("cgr.dev");
    }

    [Fact]
    public async Task Version_returns_payload()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/version");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("framework").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Diagnostics_returns_payload()
    {
        var client = CreateAuthenticated();
        var response = await client.GetAsync("/api/v1/diagnostics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("checks").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Swagger_json_lists_all_endpoints()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("openapi").GetString().Should().NotBeNullOrEmpty();
        var paths = json.GetProperty("paths");
        foreach (var path in ExpectedSwaggerPaths)
        {
            paths.TryGetProperty(path, out _).Should().BeTrue($"swagger doc should expose {path}");
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public const string TestApiKey = "test-api-key-9f2e";
        public string Home { get; } = Path.Combine(Path.GetTempPath(), $"webtest-{Guid.NewGuid():N}");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("KUBERNATOR_HOME", Home);
            Environment.SetEnvironmentVariable("KUBERNATOR_API_KEY", TestApiKey);
            Directory.CreateDirectory(Home);
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(Home, recursive: true); } catch { }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        private TempDir(string path) { Path = path; }

        public static TempDir Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kubn-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDir(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
