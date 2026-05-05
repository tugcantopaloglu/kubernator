using System.Collections.Concurrent;
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
        entries[token] = new ArtifactEntry(filePath, downloadName, DateTimeOffset.UtcNow + TokenLifetime);
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
            entries.TryRemove(token, out _);
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
                entries.TryRemove(token, out _);
            }
        }
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}

public sealed record ArtifactEntry(string FilePath, string DownloadName, DateTimeOffset ExpiresAt);
