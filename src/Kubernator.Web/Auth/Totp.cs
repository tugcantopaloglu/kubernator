using System.Security.Cryptography;
using System.Text;

namespace Kubernator.Web.Auth;

public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const int DefaultPeriodSeconds = 30;
    public const int DefaultDigits = 6;

    public static string GenerateSecret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base32Encode(bytes);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 6238 (TOTP) mandates HMAC-SHA1 for compatibility with authenticator apps.")]
    public static string ComputeCode(string base32Secret, DateTimeOffset? now = null, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        var key = Base32Decode(base32Secret);
        var counter = ((now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()) / periodSeconds;
        var counterBytes = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24)
                 | ((hash[offset + 1] & 0xFF) << 16)
                 | ((hash[offset + 2] & 0xFF) << 8)
                 | (hash[offset + 3] & 0xFF);
        var mod = (int)Math.Pow(10, digits);
        return (code % mod).ToString().PadLeft(digits, '0');
    }

    public static bool Verify(string base32Secret, string code, int allowedSkew = 1)
        => VerifyWithCounter(base32Secret, code, allowedSkew, out _);

    public static bool VerifyWithCounter(string base32Secret, string code, int allowedSkew, out long matchedCounter)
    {
        matchedCounter = -1;
        if (string.IsNullOrEmpty(base32Secret) || string.IsNullOrWhiteSpace(code)) return false;
        var trimmed = code.Replace(" ", "").Replace("-", "");
        if (trimmed.Length != DefaultDigits) return false;
        var now = DateTimeOffset.UtcNow;
        var expectedBytes = Encoding.ASCII.GetBytes(trimmed);
        for (var i = -allowedSkew; i <= allowedSkew; i++)
        {
            var window = now.AddSeconds(i * DefaultPeriodSeconds);
            var candidate = Encoding.ASCII.GetBytes(ComputeCode(base32Secret, window));
            if (CryptographicOperations.FixedTimeEquals(candidate, expectedBytes))
            {
                matchedCounter = window.ToUnixTimeSeconds() / DefaultPeriodSeconds;
                return true;
            }
        }
        return false;
    }

    public static string BuildOtpAuthUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={iss}&algorithm=SHA1&digits={DefaultDigits}&period={DefaultPeriodSeconds}";
    }

    public static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(((data.Length + 4) / 5) * 8);
        var bits = 0;
        var buffer = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }
        while (sb.Length % 8 != 0) sb.Append('=');
        return sb.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var clean = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in clean)
        {
            var idx = Base32Alphabet.IndexOf(c);
            if (idx < 0) throw new FormatException($"invalid base32 char '{c}'");
            buffer = (buffer << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return output.ToArray();
    }
}
