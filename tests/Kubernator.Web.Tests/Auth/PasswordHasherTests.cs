using Kubernator.Web.Auth;

namespace Kubernator.Web.Tests.Auth;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_returns_true()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        PasswordHasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Hash_format_includes_iteration_and_salt()
    {
        var hash = PasswordHasher.Hash("hunter2");
        hash.Split('$').Should().HaveCount(4);
        hash.Should().StartWith("pbkdf2-sha256$");
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = PasswordHasher.Hash("hunter2");
        PasswordHasher.Verify("hunter3", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_malformed_hash()
    {
        PasswordHasher.Verify("anything", "not$a$pbkdf2$hash").Should().BeFalse();
        PasswordHasher.Verify("anything", string.Empty).Should().BeFalse();
        PasswordHasher.Verify(string.Empty, "doesnt-matter").Should().BeFalse();
    }

    [Fact]
    public void Hash_throws_on_empty_password()
    {
        Action act = () => PasswordHasher.Hash(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Two_hashes_for_same_password_differ_due_to_salt()
    {
        var a = PasswordHasher.Hash("same");
        var b = PasswordHasher.Hash("same");
        a.Should().NotBe(b);
        PasswordHasher.Verify("same", a).Should().BeTrue();
        PasswordHasher.Verify("same", b).Should().BeTrue();
    }
}
