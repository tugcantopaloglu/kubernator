using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Kubernator.Web.Downloads;

public sealed class ArtifactRegistry
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, ArtifactEntry> entries = new(StringComparer.Ordinal);

    public string Register(string filePath, string downloadName)
    {
        Sweep();
        var token = NewToken();
        entries[token] = new ArtifactEntry(filePath, downloadName, DateTimeOffset.UtcNow + TokenLifetime, OwnedTempFile: false);
        return token;
    }

    public string RegisterDirectory(string directoryPath, string downloadName)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException(directoryPath);
        }
        Sweep();
        var zipPath = Path.Combine(Path.GetTempPath(), "kubernator-zip-" + Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(directoryPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        var token = NewToken();
        var nameWithExt = downloadName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? downloadName : downloadName + ".zip";
        entries[token] = new ArtifactEntry(zipPath, nameWithExt, DateTimeOffset.UtcNow + TokenLifetime, OwnedTempFile: true);
        return token;
    }

    public ArtifactEntry? Resolve(string token)
    {
        Sweep();
        if (!entries.TryGetValue(token, out var entry))
        {
            return null;
        }
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Evict(token, entry);
            return null;
        }
        return entry;
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, entry) in entries)
        {
            if (entry.ExpiresAt <= now)
            {
                Evict(token, entry);
            }
        }
    }

    private void Evict(string token, ArtifactEntry entry)
    {
        if (entries.TryRemove(token, out _) && entry.OwnedTempFile)
        {
            try { File.Delete(entry.FilePath); } catch { }
        }
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}

public sealed record ArtifactEntry(string FilePath, string DownloadName, DateTimeOffset ExpiresAt, bool OwnedTempFile);
