using Kubernator.Web.Auth;

namespace Kubernator.Web.Tests.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private readonly string home;

    public AuthServiceTests()
    {
        home = Path.Combine(Path.GetTempPath(), $"authtest-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("KUBERNATOR_HOME", home);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KUBERNATOR_HOME", null);
        try { Directory.Delete(home, recursive: true); } catch { }
    }

    [Fact]
    public async Task Setup_then_signin_succeeds_with_valid_totp()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var code = Totp.ComputeCode(setup.Account.TotpSecret);

        var result = await auth.SignInAsync("admin", "longlonglongpw", code);

        result.Outcome.Should().Be(SignInOutcome.Ok);
    }

    [Fact]
    public async Task Setup_emits_recovery_codes()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        setup.RecoveryCodes.Should().HaveCount(AuthService.RecoveryCodeCount);
        setup.RecoveryCodes.Should().OnlyContain(c => c.Length == 11 && c[5] == '-');
    }

    [Fact]
    public async Task SignIn_replay_is_rejected_on_second_use_of_same_counter()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var code = Totp.ComputeCode(setup.Account.TotpSecret);

        (await auth.SignInAsync("admin", "longlonglongpw", code)).Outcome.Should().Be(SignInOutcome.Ok);
        (await auth.SignInAsync("admin", "longlonglongpw", code)).Outcome.Should().Be(SignInOutcome.Replay);
    }

    [Fact]
    public async Task SignIn_locks_out_after_five_failures()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var code = Totp.ComputeCode(setup.Account.TotpSecret);

        for (var i = 0; i < AuthService.MaxFailedAttempts - 1; i++)
        {
            var bad = await auth.SignInAsync("admin", "wrong-password", code);
            bad.Outcome.Should().Be(SignInOutcome.InvalidCredentials);
            bad.RemainingAttempts.Should().BeGreaterThan(0);
        }

        var locked = await auth.SignInAsync("admin", "wrong-password", code);
        locked.Outcome.Should().Be(SignInOutcome.LockedOut);
        locked.LockoutUntil.Should().NotBeNull();

        (await auth.SignInAsync("admin", "longlonglongpw", code)).Outcome.Should().Be(SignInOutcome.LockedOut);
    }

    [Fact]
    public async Task RecoveryCode_succeeds_once_then_consumed()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var code = setup.RecoveryCodes[0];

        (await auth.SignInWithRecoveryAsync("admin", "longlonglongpw", code)).Outcome.Should().Be(SignInOutcome.Ok);
        (await auth.SignInWithRecoveryAsync("admin", "longlonglongpw", code)).Outcome.Should().NotBe(SignInOutcome.Ok);
    }

    [Fact]
    public async Task Wrong_password_with_recovery_increments_failures()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var code = setup.RecoveryCodes[0];

        var r = await auth.SignInWithRecoveryAsync("admin", "wrongpass", code);
        r.Outcome.Should().Be(SignInOutcome.InvalidCredentials);
        r.RemainingAttempts.Should().Be(AuthService.MaxFailedAttempts - 1);
    }

    [Fact]
    public async Task SetupTicket_is_single_use()
    {
        var auth = new AuthService();
        var setup = await auth.SetupAsync("admin", "longlonglongpw");
        var ticket = auth.IssueSetupTicket(setup);

        auth.PeekSetupTicket(ticket).Should().NotBeNull();
        auth.PeekSetupTicket(ticket).Should().NotBeNull();
        auth.ConsumeSetupTicket(ticket).Should().NotBeNull();
        auth.ConsumeSetupTicket(ticket).Should().BeNull();
    }

    [Fact]
    public async Task IsConfigured_starts_false_then_true_after_setup()
    {
        var auth = new AuthService();
        (await auth.IsConfiguredAsync()).Should().BeFalse();
        await auth.SetupAsync("admin", "longlonglongpw");
        (await auth.IsConfiguredAsync()).Should().BeTrue();
    }
}
