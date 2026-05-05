namespace Kubernator.Core.Vault;

public enum VaultEntryKind
{
    PrivateKey,
    PublicKey,
    Certificate
}

public sealed record VaultEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required VaultEntryKind Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string FileName { get; init; }
    public string? Fingerprint { get; init; }
    public bool Encrypted { get; init; }
}

public interface IKeyVault
{
    string RootDirectory { get; }

    Task<IReadOnlyList<VaultEntry>> ListAsync(CancellationToken ct = default);

    Task<VaultEntry?> GetAsync(string id, CancellationToken ct = default);

    Task<string> ResolvePathAsync(string id, CancellationToken ct = default);

    Task<VaultEntry> ImportFromBytesAsync(string name, VaultEntryKind kind, ReadOnlyMemory<byte> contents, bool encrypted, CancellationToken ct = default);

    Task<VaultEntry> ImportFromFileAsync(string name, VaultEntryKind kind, string sourcePath, bool encrypted, CancellationToken ct = default);

    Task RemoveAsync(string id, CancellationToken ct = default);
}
