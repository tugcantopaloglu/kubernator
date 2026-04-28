using System.Globalization;
using System.Security.Cryptography;

namespace Kubernator.Core.Packaging.Hashing;

internal static class Sha256Hasher
{
    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    public static string HashBytes(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    public static string FormatChecksumLine(string sha256, string relativePath) =>
        $"{sha256}  {relativePath.Replace('\\', '/')}";

    public static (string Sha256, string Path) ParseChecksumLine(string line)
    {
        var idx = line.IndexOfAny([' ', '\t']);
        if (idx <= 0)
        {
            throw new FormatException("invalid sha256sum line");
        }
        var hash = line[..idx].Trim();
        var rest = line[idx..].TrimStart(' ', '\t', '*');
        return (hash.ToLowerInvariant(), rest.Trim());
    }

    public static long ToLong(string size, IFormatProvider? formatProvider = null)
    {
        return long.Parse(size, NumberStyles.Integer, formatProvider ?? CultureInfo.InvariantCulture);
    }
}
