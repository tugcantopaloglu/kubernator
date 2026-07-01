using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kubernator.Core.Security;

namespace Kubernator.Core.Vault;

public sealed class FileKeyVault : IKeyVault, IDisposable
{
    private const string DecryptedCacheDirectoryName = ".cache";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string indexPath;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly SecretProtector protector;
    private readonly ConcurrentDictionary<string, string> decryptedCache = new(StringComparer.Ordinal);

    public FileKeyVault(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
        indexPath = Path.Combine(RootDirectory, "index.json");
        cacheDirectory = Path.Combine(RootDirectory, DecryptedCacheDirectoryName);
        protector = new SecretProtector(RootDirectory, "kubernator-vault-v1", "vault.kek", "KUBERNATOR_VAULT_KEY");
        try { Directory.Delete(cacheDirectory, recursive: true); } catch { }
    }

    public static FileKeyVault Default()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        return new FileKeyVault(Path.Combine(home, "vault"));
    }

    public string RootDirectory { get; }

    public async Task<IReadOnlyList<VaultEntry>> ListAsync(CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            return (await LoadIndexAsync(ct)).Entries
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<VaultEntry?> GetAsync(string id, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            return (await LoadIndexAsync(ct)).Entries.FirstOrDefault(e => e.Id == id);
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<string> ResolvePathAsync(string id, CancellationToken ct = default)
    {
        var entry = await GetAsync(id, ct) ?? throw new InvalidOperationException($"vault entry '{id}' not found");

        if (decryptedCache.TryGetValue(id, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        var encryptedPath = Path.Combine(RootDirectory, entry.FileName);
        var plaintext = protector.Unprotect(await File.ReadAllBytesAsync(encryptedPath, ct));

        Directory.CreateDirectory(cacheDirectory);
        var decryptedPath = Path.Combine(cacheDirectory, entry.FileName);
        await File.WriteAllBytesAsync(decryptedPath, plaintext, ct);
        TryRestrictPermissions(decryptedPath);

        decryptedCache[id] = decryptedPath;
        return decryptedPath;
    }

    public Task<VaultEntry> ImportFromFileAsync(string name, VaultEntryKind kind, string sourcePath, bool encrypted, CancellationToken ct = default)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        return ImportFromBytesAsync(name, kind, bytes, encrypted, ct);
    }

    public async Task<VaultEntry> ImportFromBytesAsync(string name, VaultEntryKind kind, ReadOnlyMemory<byte> contents, bool encrypted, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required", nameof(name));
        }
        if (contents.Length == 0)
        {
            throw new ArgumentException("contents are empty", nameof(contents));
        }

        await mutex.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var id = Guid.NewGuid().ToString("N")[..12];
            var ext = kind switch
            {
                VaultEntryKind.PrivateKey => ".key",
                VaultEntryKind.PublicKey => ".pub",
                VaultEntryKind.Certificate => ".crt",
                VaultEntryKind.SshPrivateKey => ".key",
                _ => ".bin"
            };
            var fileName = $"{id}{ext}";
            var fullPath = Path.Combine(RootDirectory, fileName);
            var fingerprint = ComputeFingerprint(contents.Span);

            var ciphertext = protector.Protect(contents.ToArray());
            await File.WriteAllBytesAsync(fullPath, ciphertext, ct);
            TryRestrictPermissions(fullPath);
            var entry = new VaultEntry
            {
                Id = id,
                Name = name,
                Kind = kind,
                CreatedAt = DateTimeOffset.UtcNow,
                FileName = fileName,
                Fingerprint = fingerprint,
                Encrypted = encrypted
            };

            var entries = index.Entries.ToList();
            entries.Add(entry);
            await SaveIndexAsync(new IndexFile { Entries = entries }, ct);
            return entry;
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var entry = index.Entries.FirstOrDefault(e => e.Id == id);
            if (entry is null)
            {
                return;
            }

            var fullPath = Path.Combine(RootDirectory, entry.FileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            EvictDecryptedCache(id);

            var remaining = index.Entries.Where(e => e.Id != id).ToList();
            await SaveIndexAsync(new IndexFile { Entries = remaining }, ct);
        }
        finally
        {
            mutex.Release();
        }
    }

    private async Task<IndexFile> LoadIndexAsync(CancellationToken ct)
    {
        if (!File.Exists(indexPath))
        {
            return new IndexFile { Entries = [] };
        }
        await using var stream = File.OpenRead(indexPath);
        var parsed = await JsonSerializer.DeserializeAsync<IndexFile>(stream, JsonOptions, ct);
        return parsed ?? new IndexFile { Entries = [] };
    }

    private async Task SaveIndexAsync(IndexFile index, CancellationToken ct)
    {
        var tmp = indexPath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, index, JsonOptions, ct);
        }
        if (File.Exists(indexPath))
        {
            File.Replace(tmp, indexPath, null);
        }
        else
        {
            File.Move(tmp, indexPath);
        }
        TryRestrictPermissions(indexPath);
    }

    private static string ComputeFingerprint(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2 + 7);
        sb.Append("sha256:");
        for (var i = 0; i < hash.Length && i < 16; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }
        return sb.ToString();
    }

    private static void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    private void EvictDecryptedCache(string id)
    {
        if (decryptedCache.TryRemove(id, out var cached))
        {
            try { File.Delete(cached); } catch { }
        }
    }

    public void Dispose()
    {
        foreach (var id in decryptedCache.Keys.ToList())
        {
            EvictDecryptedCache(id);
        }
        mutex.Dispose();
    }

    private sealed class IndexFile
    {
        public required List<VaultEntry> Entries { get; init; }
    }
}
