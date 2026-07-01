using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubernator.Core.ClusterProvisioning.Ssh;

public sealed class KnownHostsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string filePath;
    private readonly SemaphoreSlim mutex = new(1, 1);

    public KnownHostsStore(string filePath)
    {
        this.filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static KnownHostsStore Default()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        return new KnownHostsStore(Path.Combine(home, "known_hosts.json"));
    }

    public bool TryGet(string host, int port, out string fingerprint)
    {
        var index = LoadIndex();
        var key = Key(host, port);
        if (index.Entries.TryGetValue(key, out var value))
        {
            fingerprint = value;
            return true;
        }
        fingerprint = string.Empty;
        return false;
    }

    public void Trust(string host, int port, string fingerprint)
    {
        mutex.Wait();
        try
        {
            var index = LoadIndex();
            index.Entries[Key(host, port)] = fingerprint;
            SaveIndex(index);
        }
        finally
        {
            mutex.Release();
        }
    }

    public void Forget(string host, int port)
    {
        mutex.Wait();
        try
        {
            var index = LoadIndex();
            index.Entries.Remove(Key(host, port));
            SaveIndex(index);
        }
        finally
        {
            mutex.Release();
        }
    }

    private static string Key(string host, int port) => $"{host}:{port}";

    private KnownHostsFile LoadIndex()
    {
        if (!File.Exists(filePath))
        {
            return new KnownHostsFile { Entries = new Dictionary<string, string>() };
        }
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<KnownHostsFile>(json, JsonOptions)
            ?? new KnownHostsFile { Entries = new Dictionary<string, string>() };
    }

    private void SaveIndex(KnownHostsFile index)
    {
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(index, JsonOptions));
        if (File.Exists(filePath))
        {
            File.Replace(tmp, filePath, null);
        }
        else
        {
            File.Move(tmp, filePath);
        }
    }

    public void Dispose() => mutex.Dispose();

    private sealed class KnownHostsFile
    {
        public required Dictionary<string, string> Entries { get; init; }
    }
}
