namespace Kubernator.Core.Packaging.Signing;

public sealed record KeyPairFiles
{
    public required string PrivateKeyPath { get; init; }
    public required string PublicKeyPath { get; init; }
    public required bool PrivateKeyEncrypted { get; init; }
}

public sealed record SignResult
{
    public required string BlobPath { get; init; }
    public required string SignaturePath { get; init; }
    public required string PublicKeyCopyPath { get; init; }
    public required string SignatureBase64 { get; init; }
}

public sealed record SignatureVerificationResult
{
    public required bool Valid { get; init; }
    public string? Error { get; init; }
}
