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

    [Fact]
    public async Task Entry_is_stored_encrypted_at_rest()
    {
        var pem = Encoding.UTF8.GetBytes("-----BEGIN PRIVATE KEY-----\nsecret-material\n-----END PRIVATE KEY-----");
        var entry = await sut.ImportFromBytesAsync("k", VaultEntryKind.PrivateKey, pem, encrypted: false);

        var rawOnDisk = File.ReadAllBytes(Path.Combine(tempDir, entry.FileName));

        rawOnDisk.Should().NotBeEquivalentTo(pem);
        Encoding.UTF8.GetString(rawOnDisk).Should().NotContain("secret-material");
    }

    [Fact]
    public async Task Resolved_path_decrypts_to_original_content_and_is_cached()
    {
        var pem = Encoding.UTF8.GetBytes("plaintext-payload");
        var entry = await sut.ImportFromBytesAsync("k", VaultEntryKind.PrivateKey, pem, encrypted: false);

        var first = await sut.ResolvePathAsync(entry.Id);
        var second = await sut.ResolvePathAsync(entry.Id);

        first.Should().Be(second);
        File.ReadAllBytes(first).Should().BeEquivalentTo(pem);
    }

    [Fact]
    public async Task Remove_entry_also_clears_decrypted_cache_file()
    {
        var pem = Encoding.UTF8.GetBytes("cache-me");
        var entry = await sut.ImportFromBytesAsync("k", VaultEntryKind.PrivateKey, pem, encrypted: false);
        var decryptedPath = await sut.ResolvePathAsync(entry.Id);

        await sut.RemoveAsync(entry.Id);

        File.Exists(decryptedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Second_vault_instance_at_same_root_can_decrypt_existing_entries()
    {
        var pem = Encoding.UTF8.GetBytes("shared-root-secret");
        var entry = await sut.ImportFromBytesAsync("k", VaultEntryKind.PrivateKey, pem, encrypted: false);

        using var other = new FileKeyVault(tempDir);
        var path = await other.ResolvePathAsync(entry.Id);

        File.ReadAllBytes(path).Should().BeEquivalentTo(pem);
    }
}
