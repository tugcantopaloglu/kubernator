using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kubernator.Web.Api;
using Kubernator.Web.Auth;

namespace Kubernator.Web.Tests.Api;

[Collection("api-suite")]
public sealed class ClusterApiTests
{
    private static readonly string[] Amd64Only = ["amd64"];

    private readonly ApiTests.Factory factory;

    public ClusterApiTests(ApiTests.Factory factory)
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
    public async Task Pull_without_api_key_is_unauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/cluster/pull", new
        {
            outputDirectory = TestUtil.TempDir(),
            version = "v1.30.4+rke2r1",
            architectures = Amd64Only
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pull_rejects_unsupported_distro()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.PostAsJsonAsync("/api/v1/cluster/pull", new
        {
            outputDirectory = TestUtil.TempDir(),
            distro = "not-a-real-distro",
            version = "v1.30.4+rke2r1",
            architectures = Amd64Only
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pull_accepted_and_job_completes_or_fails_gracefully()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var output = TestUtil.TempDir();
        var response = await client.PostAsJsonAsync("/api/v1/cluster/pull", new
        {
            outputDirectory = output,
            version = "v0.0.0-does-not-exist",
            architectures = Amd64Only,
            includeKubectl = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("id").GetString()!;

        var done = await TestUtil.WaitForJobAsync(client, jobId, TimeSpan.FromSeconds(30));
        done.GetProperty("status").GetString().Should().BeOneOf("Succeeded", "Failed");
    }

    [Fact]
    public async Task Install_with_missing_topology_file_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.PostAsJsonAsync("/api/v1/cluster/install", new
        {
            topologyPath = Path.Combine(Path.GetTempPath(), "no-such-topology-" + Guid.NewGuid().ToString("N") + ".json")
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upgrade_with_missing_topology_file_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.PostAsJsonAsync("/api/v1/cluster/upgrade", new
        {
            topologyPath = Path.Combine(Path.GetTempPath(), "no-such-topology-" + Guid.NewGuid().ToString("N") + ".json"),
            toVersion = "v1.30.5+rke2r1"
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Status_with_missing_topology_file_returns_404()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.PostAsJsonAsync("/api/v1/cluster/status", new
        {
            topologyPath = Path.Combine(Path.GetTempPath(), "no-such-topology-" + Guid.NewGuid().ToString("N") + ".json")
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Install_with_malformed_topology_json_returns_400()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var dir = TestUtil.TempDir();
        var path = Path.Combine(dir, "topology.json");
        await File.WriteAllTextAsync(path, "{not-json");

        var response = await client.PostAsJsonAsync("/api/v1/cluster/install", new { topologyPath = path });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TrustHost_missing_host_returns_400()
    {
        var client = ClientWithKey(ApiTests.Factory.TestApiKey);
        var response = await client.PostAsJsonAsync("/api/v1/cluster/trust-host", new
        {
            host = "",
            username = "root"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Swagger_json_lists_cluster_endpoints()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = json.GetProperty("paths");
        paths.TryGetProperty("/api/v1/cluster/pull", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/cluster/install", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/cluster/upgrade", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/cluster/status", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/cluster/trust-host", out _).Should().BeTrue();
    }
}
