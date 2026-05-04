using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kubernator.Core.Updates;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient http;

    public UpdateService(HttpClient http)
    {
        this.http = http;
    }

    public async Task<UpdateCheckResult> CheckAsync(string sourceUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new ArgumentException("source URL is required", nameof(sourceUrl));
        }

        var manifest = await ReadManifestAsync(sourceUrl, ct);
        var current = KubernatorVersion.Current;
        var upgrade = CompareSemver(manifest.Version, current) > 0;

        return new UpdateCheckResult
        {
            CurrentVersion = current,
            Manifest = manifest,
            UpgradeAvailable = upgrade
        };
    }

    public async Task<UpdateApplyResult> ApplyAsync(
        string sourceUrl,
        string? runtimeIdentifierOverride,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var rid = runtimeIdentifierOverride ?? CurrentRuntimeIdentifier();
        progress?.Report($"target rid: {rid}");

        var manifest = await ReadManifestAsync(sourceUrl, ct);
        progress?.Report($"manifest version: {manifest.Version}");

        var artifact = manifest.Artifacts.FirstOrDefault(a =>
            string.Equals(a.RuntimeIdentifier, rid, StringComparison.OrdinalIgnoreCase));
        if (artifact is null)
        {
            throw new InvalidOperationException(
                $"no artifact for runtime identifier '{rid}' in manifest (available: {string.Join(", ", manifest.Artifacts.Select(a => a.RuntimeIdentifier))})");
        }

        var artifactUri = ResolveArtifactUri(sourceUrl, artifact.Url);
        progress?.Report($"downloading {artifactUri}");

        var entryPath = ResolveCurrentExecutablePath();
        var entryDir = Path.GetDirectoryName(entryPath) ?? Environment.CurrentDirectory;
        var newPath = Path.Combine(entryDir, Path.GetFileName(entryPath) + ".new");

        var actualSha = await DownloadAsync(artifactUri, newPath, artifact.SizeBytes, ct);
        progress?.Report($"sha256: {actualSha}");

        if (!string.Equals(actualSha, artifact.Sha256.ToLowerInvariant(), StringComparison.Ordinal))
        {
            File.Delete(newPath);
            throw new InvalidOperationException($"sha256 mismatch: expected {artifact.Sha256}, got {actualSha}");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(newPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var oldPath = entryPath + ".old";
        if (File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }
        if (File.Exists(entryPath))
        {
            File.Move(entryPath, oldPath);
        }
        File.Move(newPath, entryPath);
        progress?.Report($"installed at {entryPath} (previous kept at {oldPath})");

        return new UpdateApplyResult
        {
            OldExecutablePath = oldPath,
            NewExecutablePath = entryPath,
            DownloadedFromUrl = artifactUri.ToString(),
            Sha256 = actualSha,
            ToVersion = manifest.Version
        };
    }

    private async Task<ReleaseManifest> ReadManifestAsync(string sourceUrl, CancellationToken ct)
    {
        var uri = ParseUri(sourceUrl);
        Stream stream;
        if (uri.Scheme == "file")
        {
            stream = File.OpenRead(uri.LocalPath);
        }
        else
        {
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            stream = await resp.Content.ReadAsStreamAsync(ct);
        }

        await using (stream)
        {
            return await ParseManifestAsync(stream, ct);
        }
    }

    private async Task<string> DownloadAsync(Uri artifactUri, string destinationPath, long expectedSize, CancellationToken ct)
    {
        Stream input;
        if (artifactUri.Scheme == "file")
        {
            input = File.OpenRead(artifactUri.LocalPath);
        }
        else
        {
            var resp = await http.GetAsync(artifactUri, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            input = await resp.Content.ReadAsStreamAsync(ct);
        }

        await using (input)
        await using (var output = File.Create(destinationPath))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
            sha.TransformFinalBlock([], 0, 0);

            if (expectedSize > 0 && total != expectedSize)
            {
                throw new InvalidOperationException($"size mismatch: expected {expectedSize} bytes, got {total}");
            }

            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
    }

    private static Uri ParseUri(string raw)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return uri;
        }
        var full = Path.GetFullPath(raw);
        return new Uri(full);
    }

    private static Uri ResolveArtifactUri(string sourceUrl, string artifactUrl)
    {
        var manifestUri = ParseUri(sourceUrl);
        if (Uri.TryCreate(artifactUrl, UriKind.Absolute, out var abs))
        {
            return abs;
        }
        return new Uri(manifestUri, artifactUrl);
    }

    private static async Task<ReleaseManifest> ParseManifestAsync(Stream stream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var version = root.GetProperty("version").GetString()
            ?? throw new InvalidOperationException("manifest missing 'version'");
        var publishedAt = root.TryGetProperty("publishedAt", out var paProp) && paProp.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(paProp.GetString()!, CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow;
        var notes = root.TryGetProperty("notes", out var notesProp) ? notesProp.GetString() : null;
        var minFrom = root.TryGetProperty("minimumUpgradableFrom", out var mp) ? mp.GetString() : null;

        var artifacts = new List<ReleaseArtifact>();
        if (root.TryGetProperty("artifacts", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                artifacts.Add(new ReleaseArtifact
                {
                    RuntimeIdentifier = item.GetProperty("rid").GetString() ?? string.Empty,
                    Url = item.GetProperty("url").GetString() ?? string.Empty,
                    Sha256 = item.GetProperty("sha256").GetString() ?? string.Empty,
                    SizeBytes = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0,
                    FileName = item.TryGetProperty("fileName", out var fn) ? fn.GetString() : null
                });
            }
        }

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException("manifest contains no artifacts");
        }

        return new ReleaseManifest
        {
            Version = version,
            PublishedAt = publishedAt,
            Artifacts = artifacts,
            Notes = notes,
            MinimumUpgradableFrom = minFrom
        };
    }

    public static string CurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : "unknown";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };
        return $"{os}-{arch}";
    }

    public static string ResolveCurrentExecutablePath()
    {
        var p = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(p))
        {
            return p;
        }
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        return asm?.Location ?? throw new InvalidOperationException("cannot resolve current executable path");
    }

    public static int CompareSemver(string a, string b)
    {
        var pa = ParseSemver(a);
        var pb = ParseSemver(b);
        for (int i = 0; i < 3; i++)
        {
            var cmp = pa[i].CompareTo(pb[i]);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static int[] ParseSemver(string raw)
    {
        var clean = raw.TrimStart('v', 'V');
        var dash = clean.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0) clean = clean[..dash];
        var parts = clean.Split('.');
        var nums = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]);
        }
        return nums;
    }
}
