using System.Security.Cryptography;
using System.Text;

namespace Kubernator.Core.Packaging.Signing;

public sealed class CosignSigner : ICosignSigner
{
    private const int Pbkdf2Iterations = 600_000;

    public async Task<KeyPairFiles> GenerateKeyPairAsync(
        string outputDirectory,
        string baseName,
        string? passphrase,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var publicPem = ExportPublicPem(ecdsa);
        var privatePem = string.IsNullOrEmpty(passphrase)
            ? ExportPrivatePem(ecdsa)
            : ExportEncryptedPrivatePem(ecdsa, passphrase);

        var privatePath = Path.Combine(outputDirectory, baseName + ".key");
        var publicPath = Path.Combine(outputDirectory, baseName + ".pub");

        await File.WriteAllTextAsync(privatePath, privatePem, Encoding.ASCII, ct);
        await File.WriteAllTextAsync(publicPath, publicPem, Encoding.ASCII, ct);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return new KeyPairFiles
        {
            PrivateKeyPath = privatePath,
            PublicKeyPath = publicPath,
            PrivateKeyEncrypted = !string.IsNullOrEmpty(passphrase)
        };
    }

    public async Task<SignResult> SignBlobAsync(
        string blobPath,
        string privateKeyPath,
        string? passphrase,
        CancellationToken ct = default)
    {
        if (!File.Exists(blobPath))
        {
            throw new FileNotFoundException("blob not found", blobPath);
        }
        if (!File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException("private key not found", privateKeyPath);
        }

        using var ecdsa = ECDsa.Create();
        LoadPrivateKey(ecdsa, await File.ReadAllTextAsync(privateKeyPath, ct), passphrase);

        await using var stream = File.OpenRead(blobPath);
        var signature = ecdsa.SignData(stream, HashAlgorithmName.SHA256);
        var sigBase64 = Convert.ToBase64String(signature);

        var sigPath = blobPath + ".sig";
        await File.WriteAllTextAsync(sigPath, sigBase64, Encoding.ASCII, ct);

        var publicCopyPath = blobPath + ".pub";
        var publicPem = ExportPublicPem(ecdsa);
        await File.WriteAllTextAsync(publicCopyPath, publicPem, Encoding.ASCII, ct);

        return new SignResult
        {
            BlobPath = blobPath,
            SignaturePath = sigPath,
            PublicKeyCopyPath = publicCopyPath,
            SignatureBase64 = sigBase64
        };
    }

    public async Task<SignatureVerificationResult> VerifyBlobAsync(
        string blobPath,
        string signaturePath,
        string publicKeyPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(blobPath))
        {
            return new SignatureVerificationResult { Valid = false, Error = $"blob not found: {blobPath}" };
        }
        if (!File.Exists(signaturePath))
        {
            return new SignatureVerificationResult { Valid = false, Error = $"signature not found: {signaturePath}" };
        }
        if (!File.Exists(publicKeyPath))
        {
            return new SignatureVerificationResult { Valid = false, Error = $"public key not found: {publicKeyPath}" };
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(await File.ReadAllTextAsync(publicKeyPath, ct));

        var sigBase64 = (await File.ReadAllTextAsync(signaturePath, ct)).Trim();
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(sigBase64);
        }
        catch (FormatException ex)
        {
            return new SignatureVerificationResult { Valid = false, Error = $"signature is not valid base64: {ex.Message}" };
        }

        await using var stream = File.OpenRead(blobPath);
        var ok = ecdsa.VerifyData(stream, signature, HashAlgorithmName.SHA256);
        return ok
            ? new SignatureVerificationResult { Valid = true }
            : new SignatureVerificationResult { Valid = false, Error = "signature does not match public key for this blob" };
    }

    private static void LoadPrivateKey(ECDsa ecdsa, string pem, string? passphrase)
    {
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new InvalidOperationException("private key is encrypted but no passphrase was provided");
            }
            ecdsa.ImportFromEncryptedPem(pem, passphrase);
            return;
        }
        ecdsa.ImportFromPem(pem);
    }

    private static string ExportPublicPem(ECDsa ecdsa)
    {
        var der = ecdsa.ExportSubjectPublicKeyInfo();
        return WrapPem("PUBLIC KEY", der);
    }

    private static string ExportPrivatePem(ECDsa ecdsa)
    {
        var der = ecdsa.ExportPkcs8PrivateKey();
        return WrapPem("PRIVATE KEY", der);
    }

    private static string ExportEncryptedPrivatePem(ECDsa ecdsa, string passphrase)
    {
        var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, Pbkdf2Iterations);
        var der = ecdsa.ExportEncryptedPkcs8PrivateKey(passphrase, pbe);
        return WrapPem("ENCRYPTED PRIVATE KEY", der);
    }

    private static string WrapPem(string label, byte[] der)
    {
        var sb = new StringBuilder();
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        var base64 = Convert.ToBase64String(der);
        for (int i = 0; i < base64.Length; i += 64)
        {
            sb.Append(base64.AsSpan(i, Math.Min(64, base64.Length - i))).Append('\n');
        }
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }
}
