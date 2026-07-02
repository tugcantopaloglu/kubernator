using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Kubernator.Web.Storage;
using Microsoft.Data.Sqlite;

namespace Kubernator.Web.Auth;

public sealed class SqliteApiKeyStore : IApiKeyStore, IDisposable
{
    private const string PlaintextPrefix = "knk_";
    public const int DefaultRateLimitPerMinute = 120;

    private readonly string connectionString;
    private readonly SemaphoreSlim mutex = new(1, 1);

    public SqliteApiKeyStore() : this(ResolveDefaultPath())
    {
    }

    public SqliteApiKeyStore(string dbPath)
    {
        connectionString = SqliteFileStore.BuildConnectionString(dbPath);
        InitializeSchema();
        SqliteFileStore.TightenPermissions(dbPath);
    }

    private static string ResolveDefaultPath() => Path.Combine(SqliteFileStore.ResolveHome(), "auth", "api_keys.db");

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS api_keys (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                scope INTEGER NOT NULL,
                hash TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL,
                expires_at TEXT,
                last_used_at TEXT,
                disabled INTEGER NOT NULL DEFAULT 0,
                rate_limit_per_minute INTEGER NOT NULL DEFAULT 120
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection() => SqliteFileStore.OpenConnection(connectionString);

    public async Task<IReadOnlyList<ApiKeyRecord>> ListAsync(CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, scope, created_at, expires_at, last_used_at, disabled, rate_limit_per_minute
                FROM api_keys ORDER BY created_at DESC;
                """;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<ApiKeyRecord>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(MaterializeWithoutHash(reader));
            }
            return list;
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<ApiKeyRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, scope, created_at, expires_at, last_used_at, disabled, rate_limit_per_minute
                FROM api_keys WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return MaterializeWithoutHash(reader);
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<ApiKeyCreationResult> CreateAsync(CreateApiKeyOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ArgumentException("name is required", nameof(options));
        }

        var id = GenerateId();
        var (plaintext, hash) = GenerateKeyAndHash();
        var rateLimit = options.RateLimitPerMinute ?? DefaultRateLimitPerMinute;
        if (rateLimit <= 0)
        {
            throw new ArgumentException("rateLimitPerMinute must be positive", nameof(options));
        }

        var record = new ApiKeyRecord
        {
            Id = id,
            Name = options.Name.Trim(),
            Scope = options.Scope,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = options.ExpiresAt,
            LastUsedAt = null,
            Disabled = false,
            RateLimitPerMinute = rateLimit
        };

        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO api_keys (id, name, scope, hash, created_at, expires_at, last_used_at, disabled, rate_limit_per_minute)
                VALUES ($id, $name, $scope, $hash, $created, $expires, NULL, 0, $rate);
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$name", record.Name);
            cmd.Parameters.AddWithValue("$scope", (int)record.Scope);
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.Parameters.AddWithValue("$created", FormatTimestamp(record.CreatedAt));
            cmd.Parameters.AddWithValue("$expires", record.ExpiresAt is null ? DBNull.Value : (object)FormatTimestamp(record.ExpiresAt.Value));
            cmd.Parameters.AddWithValue("$rate", record.RateLimitPerMinute);
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("an api key with that name already exists", ex);
            }
        }
        finally
        {
            mutex.Release();
        }

        return new ApiKeyCreationResult { Record = record, PlaintextKey = plaintext };
    }

    public async Task<ApiKeyRecord?> ResolveByPlaintextAsync(string plaintext, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        var hash = HashPlaintext(plaintext);
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, scope, created_at, expires_at, last_used_at, disabled, rate_limit_per_minute
                FROM api_keys WHERE hash = $hash;
                """;
            cmd.Parameters.AddWithValue("$hash", hash);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return MaterializeWithoutHash(reader);
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<bool> DisableAsync(string id, bool disabled, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE api_keys SET disabled = $d WHERE id = $id;";
            cmd.Parameters.AddWithValue("$d", disabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM api_keys WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task TouchUsageAsync(string id, DateTimeOffset usedAt, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE api_keys SET last_used_at = $ts WHERE id = $id;";
            cmd.Parameters.AddWithValue("$ts", FormatTimestamp(usedAt));
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            mutex.Release();
        }
    }

    public void Dispose() => mutex.Dispose();

    private static ApiKeyRecord MaterializeWithoutHash(SqliteDataReader reader)
    {
        return new ApiKeyRecord
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Scope = (ApiKeyScope)reader.GetInt32(2),
            CreatedAt = ParseTimestamp(reader.GetString(3)),
            ExpiresAt = reader.IsDBNull(4) ? null : ParseTimestamp(reader.GetString(4)),
            LastUsedAt = reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
            Disabled = reader.GetInt32(6) != 0,
            RateLimitPerMinute = reader.GetInt32(7)
        };
    }

    private static string FormatTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string raw)
        => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string GenerateId()
    {
        Span<byte> buf = stackalloc byte[8];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexStringLower(buf);
    }

    private static (string Plaintext, string Hash) GenerateKeyAndHash()
    {
        Span<byte> raw = stackalloc byte[32];
        RandomNumberGenerator.Fill(raw);
        var b64 = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var plaintext = PlaintextPrefix + b64;
        return (plaintext, HashPlaintext(plaintext));
    }

    public static string HashPlaintext(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
