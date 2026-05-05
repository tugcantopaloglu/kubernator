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
}

public sealed record SetupResult
{
    public required AuthAccount Account { get; init; }
    public required string OtpAuthUri { get; init; }
}

public sealed class AuthService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string filePath;
    private readonly SemaphoreSlim mutex = new(1, 1);
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
            var account = new AuthAccount
            {
                Username = username.Trim(),
                PasswordHash = PasswordHasher.Hash(password),
                TotpSecret = Totp.GenerateSecret(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await SaveAsync(account, ct);
            cached = account;
            return new SetupResult
            {
                Account = account,
                OtpAuthUri = Totp.BuildOtpAuthUri("kubernator", account.Username, account.TotpSecret)
            };
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<bool> SignInAsync(string username, string password, string totpCode, CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct);
        if (account is null) return false;
        if (!string.Equals(username?.Trim(), account.Username, StringComparison.Ordinal)) return false;
        if (!PasswordHasher.Verify(password ?? string.Empty, account.PasswordHash)) return false;
        if (!Totp.Verify(account.TotpSecret, totpCode ?? string.Empty)) return false;

        await mutex.WaitAsync(ct);
        try
        {
            account.LastSignInAt = DateTimeOffset.UtcNow;
            await SaveAsync(account, ct);
            cached = account;
        }
        finally
        {
            mutex.Release();
        }
        return true;
    }

    public void Dispose() => mutex.Dispose();

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
}
