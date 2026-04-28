namespace Kubernator.Core.Packaging.Signing;

public interface ICosignSigner
{
    Task<KeyPairFiles> GenerateKeyPairAsync(string outputDirectory, string baseName, string? passphrase, CancellationToken ct = default);

    Task<SignResult> SignBlobAsync(string blobPath, string privateKeyPath, string? passphrase, CancellationToken ct = default);

    Task<SignatureVerificationResult> VerifyBlobAsync(string blobPath, string signaturePath, string publicKeyPath, CancellationToken ct = default);
}
