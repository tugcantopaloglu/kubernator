using System.Security.Cryptography;

namespace Kubernator.Web.Auth;

public sealed class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KekSize = 32;
    private const string DpapiTag = "kubernator-totp-v1";

    private readonly byte[]? unixKek;

    public SecretProtector(string authDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var envKey = Environment.GetEnvironmentVariable("KUBERNATOR_SECRET_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            try
            {
                var bytes = Convert.FromBase64String(envKey);
                if (bytes.Length != KekSize)
                {
                    throw new InvalidOperationException(
                        $"KUBERNATOR_SECRET_KEY must be base64 of {KekSize} bytes; got {bytes.Length}");
                }
                unixKek = bytes;
                return;
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("KUBERNATOR_SECRET_KEY is not valid base64");
            }
        }

        var kekPath = Path.Combine(authDirectory, ".kek");
        if (File.Exists(kekPath))
        {
            unixKek = File.ReadAllBytes(kekPath);
            if (unixKek.Length != KekSize)
            {
                throw new InvalidOperationException(
                    $"corrupt KEK at {kekPath}: expected {KekSize} bytes, got {unixKek.Length}");
            }
            return;
        }

        Directory.CreateDirectory(authDirectory);
        unixKek = RandomNumberGenerator.GetBytes(KekSize);
        File.WriteAllBytes(kekPath, unixKek);
        try { File.SetUnixFileMode(kekPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch { }
    }

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (OperatingSystem.IsWindows())
        {
            return DpapiProtect(plaintext);
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(unixKek!, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    public byte[] Unprotect(byte[] envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (OperatingSystem.IsWindows())
        {
            return DpapiUnprotect(envelope);
        }

        if (envelope.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("envelope is too short");
        }
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[envelope.Length - NonceSize - TagSize];
        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(envelope, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(envelope, NonceSize + TagSize, ciphertext, 0, ciphertext.Length);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(unixKek!, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] DpapiProtect(byte[] plaintext)
        => System.Security.Cryptography.ProtectedData.Protect(
            plaintext,
            System.Text.Encoding.UTF8.GetBytes(DpapiTag),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] DpapiUnprotect(byte[] envelope)
        => System.Security.Cryptography.ProtectedData.Unprotect(
            envelope,
            System.Text.Encoding.UTF8.GetBytes(DpapiTag),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
}
