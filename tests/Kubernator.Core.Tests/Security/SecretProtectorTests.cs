using System.Security.Cryptography;
using System.Text;
using Kubernator.Core.Security;

namespace Kubernator.Core.Tests.Security;

public sealed class SecretProtectorTests : IDisposable
{
    private readonly string tempDir;

    public SecretProtectorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"protectortest-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Protect_then_unprotect_round_trips()
    {
        var sut = new SecretProtector(tempDir, "test-purpose", "test.kek", "KUBERNATOR_TEST_SECRET_KEY_UNUSED");
        var plaintext = Encoding.UTF8.GetBytes("super-secret-value");

        var envelope = sut.Protect(plaintext);
        var roundTripped = sut.Unprotect(envelope);

        roundTripped.Should().BeEquivalentTo(plaintext);
        envelope.Should().NotBeEquivalentTo(plaintext);
    }

    [Fact]
    public void Second_instance_pointed_at_same_directory_can_decrypt()
    {
        var first = new SecretProtector(tempDir, "test-purpose", "test.kek", "KUBERNATOR_TEST_SECRET_KEY_UNUSED");
        var plaintext = Encoding.UTF8.GetBytes("shared-across-processes");
        var envelope = first.Protect(plaintext);

        var second = new SecretProtector(tempDir, "test-purpose", "test.kek", "KUBERNATOR_TEST_SECRET_KEY_UNUSED");
        second.Unprotect(envelope).Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public void Env_var_key_is_honored_and_survives_without_a_kek_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string envVar = "KUBERNATOR_TEST_SECRET_KEY_ROUNDTRIP";
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable(envVar, key);
        try
        {
            var sut = new SecretProtector(tempDir, "test-purpose", "test.kek", envVar);
            var plaintext = Encoding.UTF8.GetBytes("env-sourced-key");
            var envelope = sut.Protect(plaintext);

            sut.Unprotect(envelope).Should().BeEquivalentTo(plaintext);
            File.Exists(Path.Combine(tempDir, "test.kek")).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public void Tampered_envelope_fails_to_decrypt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = new SecretProtector(tempDir, "test-purpose", "test.kek", "KUBERNATOR_TEST_SECRET_KEY_UNUSED");
        var envelope = sut.Protect(Encoding.UTF8.GetBytes("data"));
        envelope[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() => sut.Unprotect(envelope));
    }
}
