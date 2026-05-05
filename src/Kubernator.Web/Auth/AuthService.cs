using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubernator.Web.Auth;

public sealed record AuthAccount
{
    public required string Username { get; init; }
    public required string PasswordHash { get; init; }
    public required string TotpSecret { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSignInAt { get; set; }
    public long? LastUsedTotpCounter { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public List<string> RecoveryCodeHashes { get; set; } = new();
    public bool TotpConfirmed { get; set; }
}

public sealed record SetupResult
{
    public required AuthAccount Account { get; init; }
    public required string OtpAuthUri { get; init; }
    public required IReadOnlyList<string> RecoveryCodes { get; init; }
}

public enum SignInOutcome
{
    Ok,
    InvalidCredentials,
    LockedOut,
    Replay
}

public sealed record SignInResult
{
    public required SignInOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? LockoutUntil { get; init; }
    public int RemainingAttempts { get; init; }
}

public sealed class AuthService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan SetupTicketTtl = TimeSpan.FromMinutes(10);
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public const int RecoveryCodeCount = 8;

    private readonly string filePath;
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly ConcurrentDictionary<string, (SetupResult Result, DateTimeOffset ExpiresAt)> setupTickets = new(StringComparer.Ordinal);
    private AuthAccount? cached;

    public AuthService()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        var dir = Path.Combine(home, "auth");
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, "account.json");
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        => (await GetAccountAsync(ct)) is not null;

    public async Task<AuthAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        if (cached is not null) return cached;
        await mutex.WaitAsync(ct);
        try
        {
            if (cached is not null) return cached;
            if (!File.Exists(filePath)) return null;
            await using var stream = File.OpenRead(filePath);
            cached = await JsonSerializer.DeserializeAsync<AuthAccount>(stream, JsonOptions, ct);
            return cached;
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<SetupResult> SetupAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("username is required", nameof(username));
        if (string.IsNullOrEmpty(password) || password.Length < 8) throw new ArgumentException("password must be at least 8 characters", nameof(password));

        await mutex.WaitAsync(ct);
        try
        {
            if (File.Exists(filePath))
            {
                throw new InvalidOperationException("auth is already configured; remove the account.json to reset");
            }
            var recoveryCodes = GenerateRecoveryCodes(RecoveryCodeCount);
            var account = new AuthAccount
            {
                Username = username.Trim(),
                PasswordHash = PasswordHasher.Hash(password),
                TotpSecret = Totp.GenerateSecret(),
                CreatedAt = DateTimeOffset.UtcNow,
                RecoveryCodeHashes = recoveryCodes.Select(c => PasswordHasher.Hash(NormalizeRecoveryCode(c))).ToList(),
                TotpConfirmed = false
            };
            await SaveAsync(account, ct);
            cached = account;
            return new SetupResult
            {
                Account = account,
                OtpAuthUri = Totp.BuildOtpAuthUri("kubernator", account.Username, account.TotpSecret),
                RecoveryCodes = recoveryCodes
            };
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<SignInResult> SignInAsync(string username, string password, string totpCode, CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct);
        if (account is null) return new SignInResult { Outcome = SignInOutcome.InvalidCredentials };

        var now = DateTimeOffset.UtcNow;
        if (account.LockoutUntil is { } until && until > now)
        {
            return new SignInResult { Outcome = SignInOutcome.LockedOut, LockoutUntil = until };
        }

        if (!string.Equals(username?.Trim(), account.Username, StringComparison.Ordinal)
            || !PasswordHasher.Verify(password ?? string.Empty, account.PasswordHash))
        {
            return await RegisterFailureAsync(account, ct);
        }

        if (!Totp.VerifyWithCounter(account.TotpSecret, totpCode ?? string.Empty, allowedSkew: 1, out var counter))
        {
            return await RegisterFailureAsync(account, ct);
        }
        if (account.LastUsedTotpCounter is { } prev && counter <= prev)
        {
            return new SignInResult
            {
                Outcome = SignInOutcome.Replay,
                Message = "this one-time code was already used; wait for the next one"
            };
        }

        await mutex.WaitAsync(ct);
        try
        {
            account.LastSignInAt = now;
            account.LastUsedTotpCounter = counter;
            account.FailedAttempts = 0;
            account.LockoutUntil = null;
            account.TotpConfirmed = true;
            await SaveAsync(account, ct);
            cached = account;
        }
        finally
        {
            mutex.Release();
        }
        return new SignInResult { Outcome = SignInOutcome.Ok };
    }

    public async Task<SignInResult> SignInWithRecoveryAsync(string username, string password, string recoveryCode, CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct);
        if (account is null) return new SignInResult { Outcome = SignInOutcome.InvalidCredentials };

        var now = DateTimeOffset.UtcNow;
        if (account.LockoutUntil is { } until && until > now)
        {
            return new SignInResult { Outcome = SignInOutcome.LockedOut, LockoutUntil = until };
        }

        if (!string.Equals(username?.Trim(), account.Username, StringComparison.Ordinal)
            || !PasswordHasher.Verify(password ?? string.Empty, account.PasswordHash))
        {
            return await RegisterFailureAsync(account, ct);
        }

        var normalized = NormalizeRecoveryCode(recoveryCode);
        if (string.IsNullOrEmpty(normalized)) return await RegisterFailureAsync(account, ct);

        string? matchedHash = null;
        await mutex.WaitAsync(ct);
        try
        {
            foreach (var hash in account.RecoveryCodeHashes)
            {
                if (PasswordHasher.Verify(normalized, hash))
                {
                    matchedHash = hash;
                    break;
                }
            }
            if (matchedHash is not null)
            {
                account.RecoveryCodeHashes.Remove(matchedHash);
                account.LastSignInAt = now;
                account.FailedAttempts = 0;
                account.LockoutUntil = null;
                await SaveAsync(account, ct);
                cached = account;
            }
        }
        finally
        {
            mutex.Release();
        }
        if (matchedHash is null)
        {
            return await RegisterFailureAsync(account, ct);
        }
        return new SignInResult { Outcome = SignInOutcome.Ok };
    }

    public bool TestTotpCode(string code)
    {
        var account = cached;
        if (account is null) return false;
        return Totp.Verify(account.TotpSecret, code);
    }

    public string IssueSetupTicket(SetupResult result)
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var ticket = Convert.ToHexString(bytes);
        setupTickets[ticket] = (result, DateTimeOffset.UtcNow.Add(SetupTicketTtl));
        return ticket;
    }

    public SetupResult? PeekSetupTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        if (!setupTickets.TryGetValue(ticket, out var entry)) return null;
        if (entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            setupTickets.TryRemove(ticket, out _);
            return null;
        }
        return entry.Result;
    }

    public SetupResult? ConsumeSetupTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in setupTickets)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                setupTickets.TryRemove(kvp.Key, out _);
            }
        }
        if (!setupTickets.TryRemove(ticket, out var entry)) return null;
        if (entry.ExpiresAt < now) return null;
        return entry.Result;
    }

    public void Dispose() => mutex.Dispose();

    private async Task<SignInResult> RegisterFailureAsync(AuthAccount account, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            account.FailedAttempts++;
            if (account.FailedAttempts >= MaxFailedAttempts)
            {
                account.LockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                account.FailedAttempts = 0;
                await SaveAsync(account, ct);
                cached = account;
                return new SignInResult { Outcome = SignInOutcome.LockedOut, LockoutUntil = account.LockoutUntil };
            }
            await SaveAsync(account, ct);
            cached = account;
            return new SignInResult
            {
                Outcome = SignInOutcome.InvalidCredentials,
                RemainingAttempts = MaxFailedAttempts - account.FailedAttempts
            };
        }
        finally
        {
            mutex.Release();
        }
    }

    private async Task SaveAsync(AuthAccount account, CancellationToken ct)
    {
        var tmp = filePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, account, JsonOptions, ct);
        }
        if (File.Exists(filePath)) File.Replace(tmp, filePath, null);
        else File.Move(tmp, filePath);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { }
        }
    }

    private static string NormalizeRecoveryCode(string? code)
        => (code ?? string.Empty).Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();

    private static IReadOnlyList<string> GenerateRecoveryCodes(int count)
    {
        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(5);
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            list.Add($"{hex[..5]}-{hex[5..]}");
        }
        return list;
    }
}
