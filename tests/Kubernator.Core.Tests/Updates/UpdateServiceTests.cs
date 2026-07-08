using System.Net.Http;
using FluentAssertions;
using Kubernator.Core.Updates;

namespace Kubernator.Core.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string scratch;
    private readonly UpdateService sut = new(new HttpClient());

    public UpdateServiceTests()
    {
        scratch = Path.Combine(Path.GetTempPath(), "kubernator-updtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(scratch, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteManifest(string json)
    {
        var path = Path.Combine(scratch, "release.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Parses_wellformed_manifest_and_flags_upgrade()
    {
        var path = WriteManifest("""
        {
          "version": "999.0.0",
          "publishedAt": "2026-07-01T00:00:00Z",
          "notes": "test",
          "artifacts": [
            { "rid": "win-x64", "url": "https://example.com/a.zip", "sha256": "abc", "size": 10, "fileName": "a.zip" }
          ]
        }
        """);

        var result = await sut.CheckAsync(path);

        result.Manifest.Version.Should().Be("999.0.0");
        result.Manifest.Artifacts.Should().ContainSingle().Which.RuntimeIdentifier.Should().Be("win-x64");
        result.UpgradeAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_required_artifact_field_throws_friendly_error_not_keynotfound()
    {
        var path = WriteManifest("""
        {
          "version": "1.0.0",
          "artifacts": [ { "url": "https://example.com/a.zip", "sha256": "abc", "size": 10 } ]
        }
        """);

        var act = () => sut.CheckAsync(path);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("rid");
    }

    [Fact]
    public async Task Empty_artifacts_throws_invalidoperation()
    {
        var path = WriteManifest("""{ "version": "1.0.0", "artifacts": [] }""");

        var act = () => sut.CheckAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Malformed_json_throws_invalidoperation_not_jsonexception()
    {
        var path = WriteManifest("not json {");

        var act = () => sut.CheckAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Missing_version_throws_friendly_error()
    {
        var path = WriteManifest("""
        { "artifacts": [ { "rid": "win-x64", "url": "https://example.com/a.zip", "sha256": "abc", "size": 10 } ] }
        """);

        var act = () => sut.CheckAsync(path);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("version");
    }
}
