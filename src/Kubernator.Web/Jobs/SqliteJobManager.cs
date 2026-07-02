using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Kubernator.Web.Jobs;

public sealed class SqliteJobManager : IJobManager, IDisposable
{
    public const int ProgressBuffer = 200;

    private readonly string connectionString;
    private readonly object mutex = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> running = new(StringComparer.Ordinal);
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = false });

    public ChannelReader<string> Reader => queue.Reader;

    public SqliteJobManager() : this(ResolveDefaultPath())
    {
    }

    public SqliteJobManager(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
        InitializeSchema();
        TightenPermissions(dbPath);
        RequeueOrphanedJobs();
    }

    private static string ResolveDefaultPath()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        return Path.Combine(home, "jobs", "jobs.db");
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS jobs (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                status TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                result_json TEXT,
                error TEXT,
                key_id TEXT,
                key_name TEXT,
                created_at TEXT NOT NULL,
                started_at TEXT,
                completed_at TEXT
            );
            CREATE TABLE IF NOT EXISTS job_progress (
                job_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                message TEXT NOT NULL,
                PRIMARY KEY (job_id, seq)
            );
            CREATE INDEX IF NOT EXISTS idx_job_progress_job ON job_progress(job_id);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void TightenPermissions(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch { }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    // Jobs still marked Running belonged to a process that died before finishing them;
    // they can't be resumed mid-flight (no checkpointing), so they go back to the front
    // of the queue and start over from scratch.
    private void RequeueOrphanedJobs()
    {
        lock (mutex)
        {
            using var conn = OpenConnection();
            using (var reset = conn.CreateCommand())
            {
                reset.CommandText = "UPDATE jobs SET status = $queued, started_at = NULL WHERE status = $running;";
                reset.Parameters.AddWithValue("$queued", JobStatus.Queued.ToString());
                reset.Parameters.AddWithValue("$running", JobStatus.Running.ToString());
                reset.ExecuteNonQuery();
            }

            using var select = conn.CreateCommand();
            select.CommandText = "SELECT id FROM jobs WHERE status = $queued ORDER BY created_at ASC;";
            select.Parameters.AddWithValue("$queued", JobStatus.Queued.ToString());
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                queue.Writer.TryWrite(reader.GetString(0));
            }
        }
    }

    public JobRecord Enqueue<TPayload>(string kind, TPayload payload, string? keyId = null, string? keyName = null)
    {
        var id = GenerateId();
        var payloadJson = JsonSerializer.Serialize(payload, JobJson.Options);
        var createdAt = DateTimeOffset.UtcNow;

        lock (mutex)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO jobs (id, kind, status, payload_json, key_id, key_name, created_at)
                VALUES ($id, $kind, $status, $payload, $keyId, $keyName, $created);
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$status", JobStatus.Queued.ToString());
            cmd.Parameters.AddWithValue("$payload", payloadJson);
            cmd.Parameters.AddWithValue("$keyId", (object?)keyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$keyName", (object?)keyName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", FormatTimestamp(createdAt));
            cmd.ExecuteNonQuery();
        }

        queue.Writer.TryWrite(id);

        return new JobRecord
        {
            Id = id,
            Kind = kind,
            Status = JobStatus.Queued,
            CreatedAt = createdAt,
            Progress = [],
            KeyId = keyId,
            KeyName = keyName
        };
    }

    public JobRecord? Get(string id)
    {
        lock (mutex)
        {
            using var conn = OpenConnection();
            return ReadJob(conn, id);
        }
    }

    public IReadOnlyList<JobRecord> List(int limit = 100)
    {
        lock (mutex)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM jobs ORDER BY created_at DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);
            var ids = new List<string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }
            return ids.Select(i => ReadJob(conn, i)!).ToArray();
        }
    }

    public bool Cancel(string id)
    {
        lock (mutex)
        {
            if (running.TryGetValue(id, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
                return true;
            }

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE jobs SET status = $cancelled, error = 'cancelled', completed_at = $now
                WHERE id = $id AND status = $queued;
                """;
            cmd.Parameters.AddWithValue("$cancelled", JobStatus.Cancelled.ToString());
            cmd.Parameters.AddWithValue("$now", FormatTimestamp(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$queued", JobStatus.Queued.ToString());
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Atomically transitions a queued job to Running and hands back its kind/payload for
    /// execution, or null if the job was already cancelled/removed while it sat in the queue.
    /// </summary>
    internal JobExecution? BeginExecution(string id)
    {
        lock (mutex)
        {
            using var conn = OpenConnection();
            string kind, payloadJson;
            using (var select = conn.CreateCommand())
            {
                select.CommandText = "SELECT kind, status, payload_json FROM jobs WHERE id = $id;";
                select.Parameters.AddWithValue("$id", id);
                using var reader = select.ExecuteReader();
                if (!reader.Read()) return null;
                kind = reader.GetString(0);
                var status = reader.GetString(1);
                payloadJson = reader.GetString(2);
                if (status != JobStatus.Queued.ToString()) return null;
            }

            using (var update = conn.CreateCommand())
            {
                update.CommandText = "UPDATE jobs SET status = $running, started_at = $now WHERE id = $id;";
                update.Parameters.AddWithValue("$running", JobStatus.Running.ToString());
                update.Parameters.AddWithValue("$now", FormatTimestamp(DateTimeOffset.UtcNow));
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            var cts = new CancellationTokenSource();
            running[id] = cts;
            return new JobExecution(kind, payloadJson, cts);
        }
    }

    internal void AddProgress(string id, string message)
    {
        lock (mutex)
        {
            using var conn = OpenConnection();
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO job_progress (job_id, seq, timestamp, message)
                    VALUES ($id, (SELECT COALESCE(MAX(seq), 0) + 1 FROM job_progress WHERE job_id = $id), $ts, $message);
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$ts", FormatTimestamp(DateTimeOffset.UtcNow));
                insert.Parameters.AddWithValue("$message", message);
                insert.ExecuteNonQuery();
            }

            using var trim = conn.CreateCommand();
            trim.CommandText = """
                DELETE FROM job_progress
                WHERE job_id = $id
                  AND seq <= (SELECT COALESCE(MAX(seq), 0) - $buffer FROM job_progress WHERE job_id = $id);
                """;
            trim.Parameters.AddWithValue("$id", id);
            trim.Parameters.AddWithValue("$buffer", ProgressBuffer);
            trim.ExecuteNonQuery();
        }
    }

    internal void Complete(string id, JobStatus status, object? result, string? error)
    {
        var resultJson = result is null ? null : JsonSerializer.Serialize(result, result.GetType(), JobJson.Options);
        lock (mutex)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE jobs SET status = $status, result_json = $result, error = $error, completed_at = $now
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$status", status.ToString());
            cmd.Parameters.AddWithValue("$result", (object?)resultJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", FormatTimestamp(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    internal void EndExecution(string id)
    {
        lock (mutex)
        {
            if (running.TryRemove(id, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    private static JobRecord? ReadJob(SqliteConnection conn, string id)
    {
        JobRecord? record = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, kind, status, result_json, error, key_id, key_name, created_at, started_at, completed_at
                FROM jobs WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var resultJson = reader.IsDBNull(3) ? null : reader.GetString(3);
            record = new JobRecord
            {
                Id = reader.GetString(0),
                Kind = reader.GetString(1),
                Status = Enum.Parse<JobStatus>(reader.GetString(2)),
                Result = resultJson is null ? null : JsonSerializer.Deserialize<JsonElement>(resultJson),
                Error = reader.IsDBNull(4) ? null : reader.GetString(4),
                KeyId = reader.IsDBNull(5) ? null : reader.GetString(5),
                KeyName = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = ParseTimestamp(reader.GetString(7)),
                StartedAt = reader.IsDBNull(8) ? null : ParseTimestamp(reader.GetString(8)),
                CompletedAt = reader.IsDBNull(9) ? null : ParseTimestamp(reader.GetString(9)),
                Progress = []
            };
        }

        var progress = new List<JobProgressEntry>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT timestamp, message FROM job_progress WHERE job_id = $id ORDER BY seq ASC;";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                progress.Add(new JobProgressEntry { Timestamp = ParseTimestamp(reader.GetString(0)), Message = reader.GetString(1) });
            }
        }

        return record with { Progress = progress };
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

    public void Dispose()
    {
        foreach (var cts in running.Values)
        {
            cts.Dispose();
        }
    }
}

internal sealed record JobExecution(string Kind, string PayloadJson, CancellationTokenSource Cts);
