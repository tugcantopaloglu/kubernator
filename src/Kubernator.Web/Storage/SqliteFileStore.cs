using Microsoft.Data.Sqlite;

namespace Kubernator.Web.Storage;

/// <summary>Shared plumbing for the small connection-per-call SQLite stores in this
/// project (API keys, jobs): resolving KUBERNATOR_HOME, building a WAL-mode connection
/// string, and tightening file permissions on non-Windows hosts.</summary>
internal static class SqliteFileStore
{
    public static string ResolveHome()
        => Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");

    public static string BuildConnectionString(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    public static SqliteConnection OpenConnection(string connectionString)
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    public static void TightenPermissions(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch { }
    }
}
