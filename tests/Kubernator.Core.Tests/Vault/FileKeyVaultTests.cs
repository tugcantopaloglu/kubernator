using System.Text;
using Kubernator.Core.Vault;

namespace Kubernator.Core.Tests.Vault;

public sealed class FileKeyVaultTests : IDisposable
{
    private readonly string tempDir;
    private readonly FileKeyVault sut;

    public FileKeyVaultTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"vaulttest-{Guid.NewGuid():N}");
        sut = new FileKeyVault(tempDir);
    }

    public void Dispose()
    {
        sut.Dispose();
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Empty_vault_lists_zero_entries()
    {
        var entries = await sut.ListAsync();
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_then_list_returns_entry()
    {
        var pem = Encoding.UTF8.GetBytes("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----");
        var entry = await sut.ImportFromBytesAsync("test-key", VaultEntryKind.PublicKey, pem, encrypted: false);

        entry.Name.Should().Be("test-key");
        entry.Kind.Should().Be(VaultEntryKind.PublicKey);
        entry.Encrypted.Should().BeFalse();
        entry.Fingerprint.Should().StartWith("sha256:");

        var list = await sut.ListAsync();
        list.Should().ContainSingle().Which.Id.Should().Be(entry.Id);
    }

    [Fact]
    public async Task Resolve_path_round_trip()
    {
        var pem = Encoding.UTF8.GetBytes("certbody");
        var entry = await sut.ImportFromBytesAsync("c", VaultEntryKind.Certificate, pem, encrypted: false);
        var path = await sut.ResolvePathAsync(entry.Id);
        File.Exists(path).Should().BeTrue();
        File.ReadAllBytes(path).Should().BeEquivalentTo(pem);
    }

    [Fact]
    public async Task Remove_entry_clears_disk_and_index()
    {
        var pem = Encoding.UTF8.GetBytes("x");
        var entry = await sut.ImportFromBytesAsync("k", VaultEntryKind.PrivateKey, pem, encrypted: true);
        var path = await sut.ResolvePathAsync(entry.Id);

        await sut.RemoveAsync(entry.Id);

        File.Exists(path).Should().BeFalse();
        (await sut.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_payload_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ImportFromBytesAsync("name", VaultEntryKind.PublicKey, Array.Empty<byte>(), false));
    }
}
