using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Kubernator.Core.Security;
using Microsoft.Data.Sqlite;

namespace Kubernator.Web.Auth;

public sealed record AuthAccount
{
    public required long Id { get; init; }
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
    private static readonly TimeSpan SetupTicketTtl = TimeSpan.FromMinutes(10);
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public const int RecoveryCodeCount = 8;

    private readonly string connectionString;
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly ConcurrentDictionary<string, (SetupResult Result, DateTimeOffset ExpiresAt)> setupTickets = new(StringComparer.Ordinal);
    private readonly SecretProtector protector;
    private AuthAccount? cached;

    public AuthService()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        var dir = Path.Combine(home, "auth");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "auth.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
        protector = new SecretProtector(dir, "kubernator-totp-v1", ".kek", "KUBERNATOR_SECRET_KEY");
        InitializeSchema();
        TightenDbFilePermissions(dbPath);
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS schema_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                totp_secret_protected BLOB NOT NULL,
                created_at TEXT NOT NULL,
                last_signin_at TEXT,
                last_used_totp_counter INTEGER,
                failed_attempts INTEGER NOT NULL DEFAULT 0,
                lockout_until TEXT,
                totp_confirmed INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS recovery_codes (
                id INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
                hash TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_meta(key, value) VALUES('version', '1');
            """;
        cmd.ExecuteNonQuery();
    }

    private static void TightenDbFilePermissions(string dbPath)
    {
        if (OperatingSystem.IsWindows()) return;
        if (!File.Exists(dbPath)) return;
        try { File.SetUnixFileMode(dbPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch { }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        => Task.FromResult(GetAccount() is not null);

    public Task<AuthAccount?> GetAccountAsync(CancellationToken ct = default)
        => Task.FromResult(GetAccount());

    private AuthAccount? GetAccount()
    {
        if (cached is not null) return cached;
        mutex.Wait();
        try
        {
            if (cached is not null) return cached;
            using var conn = OpenConnection();
            cached = LoadFirstAccount(conn);
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
            using var conn = OpenConnection();
            using var existing = conn.CreateCommand();
            existing.CommandText = "SELECT COUNT(*) FROM accounts;";
            var count = (long)(existing.ExecuteScalar() ?? 0L);
            if (count > 0)
            {
                throw new InvalidOperationException("auth is already configured; remove the auth.db to reset");
            }

            var recoveryCodes = GenerateRecoveryCodes(RecoveryCodeCount);
            var totpSecret = Totp.GenerateSecret();
            var totpProtected = protector.Protect(System.Text.Encoding.UTF8.GetBytes(totpSecret));

            using var tx = conn.BeginTransaction();

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO accounts (username, password_hash, totp_secret_protected, created_at, totp_confirmed)
                    VALUES ($u, $p, $s, $c, 0);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$u", username.Trim());
                insert.Parameters.AddWithValue("$p", PasswordHasher.Hash(password));
                insert.Parameters.AddWithValue("$s", totpProtected);
                insert.Parameters.AddWithValue("$c", FormatTimestamp(DateTimeOffset.UtcNow));
                var idObj = insert.ExecuteScalar();
                var id = (long)(idObj ?? throw new InvalidOperationException("insert returned no id"));

                using (var insertCode = conn.CreateCommand())
                {
                    insertCode.Transaction = tx;
                    insertCode.CommandText = "INSERT INTO recovery_codes (account_id, hash) VALUES ($a, $h);";
                    var pAcc = insertCode.Parameters.Add("$a", SqliteType.Integer);
                    var pHash = insertCode.Parameters.Add("$h", SqliteType.Text);
                    foreach (var code in recoveryCodes)
                    {
                        pAcc.Value = id;
                        pHash.Value = PasswordHasher.Hash(NormalizeRecoveryCode(code));
                        insertCode.ExecuteNonQuery();
                    }
                }

                tx.Commit();

                var account = new AuthAccount
                {
                    Id = id,
                    Username = username.Trim(),
                    PasswordHash = string.Empty,
                    TotpSecret = totpSecret,
                    CreatedAt = DateTimeOffset.UtcNow,
                    TotpConfirmed = false
                };
                cached = LoadAccount(conn, id);

                return new SetupResult
                {
                    Account = account,
                    OtpAuthUri = Totp.BuildOtpAuthUri("kubernator", account.Username, totpSecret),
                    RecoveryCodes = recoveryCodes
                };
            }
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<SignInResult> SignInAsync(string username, string password, string totpCode, CancellationToken ct = default)
    {
        var account = GetAccount();
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
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE accounts
                SET last_signin_at = $ls,
                    last_used_totp_counter = $ctr,
                    failed_attempts = 0,
                    lockout_until = NULL,
                    totp_confirmed = 1
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$ls", FormatTimestamp(now));
            cmd.Parameters.AddWithValue("$ctr", counter);
            cmd.Parameters.AddWithValue("$id", account.Id);
            cmd.ExecuteNonQuery();
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
        var account = GetAccount();
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
                using var conn = OpenConnection();
                using var tx = conn.BeginTransaction();
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM recovery_codes WHERE account_id = $a AND hash = $h;";
                    del.Parameters.AddWithValue("$a", account.Id);
                    del.Parameters.AddWithValue("$h", matchedHash);
                    del.ExecuteNonQuery();
                }
                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = """
                        UPDATE accounts
                        SET last_signin_at = $ls, failed_attempts = 0, lockout_until = NULL
                        WHERE id = $id;
                        """;
                    upd.Parameters.AddWithValue("$ls", FormatTimestamp(now));
                    upd.Parameters.AddWithValue("$id", account.Id);
                    upd.ExecuteNonQuery();
                }
                tx.Commit();
                account.RecoveryCodeHashes.Remove(matchedHash);
                account.LastSignInAt = now;
                account.FailedAttempts = 0;
                account.LockoutUntil = null;
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
        var account = cached ?? GetAccount();
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
            using var conn = OpenConnection();
            if (account.FailedAttempts >= MaxFailedAttempts)
            {
                account.LockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                account.FailedAttempts = 0;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE accounts SET failed_attempts = 0, lockout_until = $lu WHERE id = $id;
                    """;
                cmd.Parameters.AddWithValue("$lu", FormatTimestamp(account.LockoutUntil!.Value));
                cmd.Parameters.AddWithValue("$id", account.Id);
                cmd.ExecuteNonQuery();
                cached = account;
                return new SignInResult { Outcome = SignInOutcome.LockedOut, LockoutUntil = account.LockoutUntil };
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE accounts SET failed_attempts = $fa WHERE id = $id;";
                cmd.Parameters.AddWithValue("$fa", account.FailedAttempts);
                cmd.Parameters.AddWithValue("$id", account.Id);
                cmd.ExecuteNonQuery();
            }
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

    private AuthAccount? LoadFirstAccount(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, password_hash, totp_secret_protected, created_at,
                   last_signin_at, last_used_totp_counter, failed_attempts, lockout_until, totp_confirmed
            FROM accounts ORDER BY id LIMIT 1;
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MaterializeAccount(conn, reader);
    }

    private AuthAccount? LoadAccount(SqliteConnection conn, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, password_hash, totp_secret_protected, created_at,
                   last_signin_at, last_used_totp_counter, failed_attempts, lockout_until, totp_confirmed
            FROM accounts WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MaterializeAccount(conn, reader);
    }

    private AuthAccount MaterializeAccount(SqliteConnection conn, SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var username = reader.GetString(1);
        var passwordHash = reader.GetString(2);
        var protectedBytes = (byte[])reader.GetValue(3);
        var totpSecret = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
        var createdAt = ParseTimestamp(reader.GetString(4));
        DateTimeOffset? lastSignIn = reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5));
        long? lastCounter = reader.IsDBNull(6) ? null : reader.GetInt64(6);
        var failed = reader.GetInt32(7);
        DateTimeOffset? lockout = reader.IsDBNull(8) ? null : ParseTimestamp(reader.GetString(8));
        var confirmed = reader.GetInt32(9) != 0;

        var account = new AuthAccount
        {
            Id = id,
            Username = username,
            PasswordHash = passwordHash,
            TotpSecret = totpSecret,
            CreatedAt = createdAt,
            LastSignInAt = lastSignIn,
            LastUsedTotpCounter = lastCounter,
            FailedAttempts = failed,
            LockoutUntil = lockout,
            TotpConfirmed = confirmed
        };

        using (var rcCmd = conn.CreateCommand())
        {
            rcCmd.CommandText = "SELECT hash FROM recovery_codes WHERE account_id = $a;";
            rcCmd.Parameters.AddWithValue("$a", id);
            using var rcReader = rcCmd.ExecuteReader();
            while (rcReader.Read())
            {
                account.RecoveryCodeHashes.Add(rcReader.GetString(0));
            }
        }
        return account;
    }

    private static string FormatTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
