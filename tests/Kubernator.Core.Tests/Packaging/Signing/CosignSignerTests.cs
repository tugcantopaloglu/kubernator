using Kubernator.Core.Packaging.Signing;
using Kubernator.Core.Tests.Fixtures;

namespace Kubernator.Core.Tests.Packaging.Signing;

public sealed class CosignSignerTests
{
    private readonly CosignSigner signer = new();

    [Fact]
    public async Task Round_trip_unencrypted_key_signs_and_verifies()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        var keys = await signer.GenerateKeyPairAsync(t.Path, "k", passphrase: null);
        keys.PrivateKeyEncrypted.Should().BeFalse();
        File.Exists(keys.PrivateKeyPath).Should().BeTrue();
        File.Exists(keys.PublicKeyPath).Should().BeTrue();

        var sign = await signer.SignBlobAsync(blob, keys.PrivateKeyPath, passphrase: null);
        sign.SignatureBase64.Should().NotBeNullOrEmpty();
        File.Exists(sign.SignaturePath).Should().BeTrue();

        var verify = await signer.VerifyBlobAsync(blob, sign.SignaturePath, keys.PublicKeyPath);
        verify.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task Round_trip_encrypted_key_signs_and_verifies_with_passphrase()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [42, 43, 44, 45]);

        var keys = await signer.GenerateKeyPairAsync(t.Path, "secret", passphrase: "correct horse battery staple");
        keys.PrivateKeyEncrypted.Should().BeTrue();
        var keyText = await File.ReadAllTextAsync(keys.PrivateKeyPath);
        keyText.Should().Contain("ENCRYPTED PRIVATE KEY");

        var sign = await signer.SignBlobAsync(blob, keys.PrivateKeyPath, passphrase: "correct horse battery staple");
        var verify = await signer.VerifyBlobAsync(blob, sign.SignaturePath, keys.PublicKeyPath);
        verify.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task Sign_with_wrong_passphrase_throws()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [9]);
        var keys = await signer.GenerateKeyPairAsync(t.Path, "x", passphrase: "right");

        var act = () => signer.SignBlobAsync(blob, keys.PrivateKeyPath, passphrase: "wrong");

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public async Task Verify_fails_when_blob_is_tampered()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [1, 2, 3]);
        var keys = await signer.GenerateKeyPairAsync(t.Path, "k", passphrase: null);
        var sign = await signer.SignBlobAsync(blob, keys.PrivateKeyPath, passphrase: null);

        await File.WriteAllBytesAsync(blob, [1, 2, 3, 99]);

        var verify = await signer.VerifyBlobAsync(blob, sign.SignaturePath, keys.PublicKeyPath);
        verify.Valid.Should().BeFalse();
    }

    [Fact]
    public async Task Signature_is_der_encoded_ecdsa_p256_compatible_with_cosign()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [1, 2, 3, 4, 5]);
        var keys = await signer.GenerateKeyPairAsync(t.Path, "k", passphrase: null);
        var sign = await signer.SignBlobAsync(blob, keys.PrivateKeyPath, passphrase: null);

        var raw = Convert.FromBase64String(await File.ReadAllTextAsync(sign.SignaturePath));
        raw[0].Should().Be(0x30, because: "DER SEQUENCE tag");
        raw.Length.Should().BeInRange(70, 72, because: "DER ECDSA P-256 signatures are 70-72 bytes");

        var publicPem = await File.ReadAllTextAsync(keys.PublicKeyPath);
        publicPem.Should().StartWith("-----BEGIN PUBLIC KEY-----");
        publicPem.TrimEnd().Should().EndWith("-----END PUBLIC KEY-----");
    }

    [Fact]
    public async Task Verify_fails_when_signed_by_a_different_key()
    {
        using var t = TempPublishOutput.Create();
        var blob = Path.Combine(t.Path, "blob.bin");
        await File.WriteAllBytesAsync(blob, [9, 8, 7]);

        var keysA = await signer.GenerateKeyPairAsync(t.Path, "a", passphrase: null);
        var keysB = await signer.GenerateKeyPairAsync(t.Path, "b", passphrase: null);
        var sign = await signer.SignBlobAsync(blob, keysA.PrivateKeyPath, passphrase: null);

        var verify = await signer.VerifyBlobAsync(blob, sign.SignaturePath, keysB.PublicKeyPath);

        verify.Valid.Should().BeFalse();
    }
}
