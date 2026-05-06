using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kubernator.Core.Containers;
using Kubernator.Core.Packaging.Hashing;

namespace Kubernator.Core.AirGapped;

public sealed partial class ImageBundleService : IImageBundleService
{
    private const string SchemaVersion = "1.0";
    private const string ManifestFileName = "images.manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<ImageBundleResult> PullAsync(
        ImageBundleOptions options,
        IContainerEngine engine,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (options.References.Count == 0)
        {
            throw new ArgumentException("at least one image reference is required", nameof(options));
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var entries = new List<ImageBundleEntry>();
        foreach (var rawRef in options.References)
        {
            var reference = NormalizeReference(rawRef);
            progress?.Report($"pulling {reference}");

            if (options.ForcePull || await engine.GetImageAsync(reference, ct) is null)
            {
                await foreach (var line in engine.PullImageAsync(reference, options.Platform, ct))
                {
                    progress?.Report(line);
                }
            }
            else
            {
                progress?.Report($"already present: {reference}");
            }

            var info = await engine.GetImageAsync(reference, ct)
                ?? throw new InvalidOperationException($"image not found after pull: {reference}");

            var fileName = SanitizeFilename(reference) + ".tar";
            var tarPath = Path.Combine(options.OutputDirectory, fileName);
            progress?.Report($"saving {reference} -> {fileName}");
            await engine.SaveImageAsync(reference, tarPath, ct);

            var sha = await Sha256Hasher.HashFileAsync(tarPath, ct);
            entries.Add(new ImageBundleEntry
            {
                Reference = reference,
                TarRelativePath = fileName,
                SizeBytes = new FileInfo(tarPath).Length,
                Sha256 = sha,
                ImageId = info.Id,
                Platform = options.Platform,
                Tags = info.Tags
            });
        }

        var manifest = new ImageBundleManifest
        {
            SchemaVersion = SchemaVersion,
            Tool = "kubernator",
            ToolVersion = ToolVersion(),
            CreatedAt = DateTimeOffset.UtcNow,
            Images = entries
        };

        var manifestPath = Path.Combine(options.OutputDirectory, ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct);

        string? combinedPath = null;
        if (options.CombineIntoSingleArchive)
        {
            combinedPath = Path.Combine(options.OutputDirectory, options.CombinedArchiveName);
            progress?.Report($"compressing into {Path.GetFileName(combinedPath)}");
            await CreateTarGzAsync(options.OutputDirectory, combinedPath, ct);
        }

        return new ImageBundleResult
        {
            OutputDirectory = options.OutputDirectory,
            ManifestPath = manifestPath,
            Manifest = manifest,
            CombinedArchivePath = combinedPath
        };
    }

    public async Task<ImageRehostResult> RehostAsync(
        ImageRehostOptions options,
        IContainerEngine engine,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(options.BundleDirectory, ct)
            ?? throw new InvalidOperationException($"no {ManifestFileName} found in {options.BundleDirectory}");

        var targetRegistry = options.TargetRegistry.TrimEnd('/');
        var targetNs = string.IsNullOrEmpty(options.TargetNamespace)
            ? string.Empty
            : "/" + options.TargetNamespace.Trim('/');

        var pushed = new List<ImagePushPlan>();
        var errors = new List<string>();

        foreach (var entry in manifest.Images)
        {
            ct.ThrowIfCancellationRequested();
            var tarPath = Path.Combine(options.BundleDirectory, entry.TarRelativePath);
            if (options.LoadBeforePush)
            {
                if (!File.Exists(tarPath))
                {
                    errors.Add($"missing image tar: {entry.TarRelativePath}");
                    continue;
                }
                progress?.Report($"loading {entry.TarRelativePath}");
                try
                {
                    await engine.LoadImageAsync(tarPath, ct);
                }
                catch (Exception ex)
                {
                    errors.Add($"load {entry.Reference}: {ex.Message}");
                    continue;
                }
            }

            var rewritten = RewriteRegistry(entry.Reference, targetRegistry, targetNs);
            progress?.Report($"tagging {entry.Reference} -> {rewritten}");
            try
            {
                await engine.TagImageAsync(entry.Reference, rewritten, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"tag {entry.Reference}: {ex.Message}");
                continue;
            }

            progress?.Report($"pushing {rewritten}");
            try
            {
                await foreach (var line in engine.PushImageAsync(rewritten, ct))
                {
                    progress?.Report(line);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"push {rewritten}: {ex.Message}");
                continue;
            }

            pushed.Add(new ImagePushPlan
            {
                SourceReference = entry.Reference,
                TargetReference = rewritten,
                TarRelativePath = entry.TarRelativePath
            });
        }

        var rewrittenFiles = new List<string>();
        if (options.RewriteManifestImages && !string.IsNullOrEmpty(options.ManifestsDirectory))
        {
            rewrittenFiles = await RewriteManifestsAsync(options.ManifestsDirectory!, pushed, progress, ct);
        }

        return new ImageRehostResult
        {
            Pushed = pushed,
            RewrittenManifestFiles = rewrittenFiles,
            Errors = errors
        };
    }

    public async Task<ImageBundleManifest?> ReadManifestAsync(string bundleDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(bundleDirectory, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ImageBundleManifest>(json, JsonOptions);
    }

    private static async Task<List<string>> RewriteManifestsAsync(
        string manifestsDirectory,
        IReadOnlyList<ImagePushPlan> pushed,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(manifestsDirectory))
        {
            return [];
        }

        var rewriteMap = pushed.ToDictionary(p => p.SourceReference, p => p.TargetReference, StringComparer.Ordinal);
        var changedFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(manifestsDirectory, "*.yaml", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var original = await File.ReadAllTextAsync(file, ct);
            var updated = original;
            foreach (var (source, target) in rewriteMap)
            {
                updated = ReplaceImageReference(updated, source, target);
            }
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(file, updated, ct);
                changedFiles.Add(file);
                progress?.Report($"rewrote {Path.GetFileName(file)}");
            }
        }

        return changedFiles;
    }

    private static string ReplaceImageReference(string yaml, string source, string target)
    {
        var pattern = "image:\\s*[\"']?" + Regex.Escape(source) + "[\"']?";
        return Regex.Replace(yaml, pattern, $"image: {target}", RegexOptions.None, TimeSpan.FromSeconds(2));
    }

    private static string RewriteRegistry(string reference, string targetRegistry, string targetNs)
    {
        var atIdx = reference.IndexOf('@', StringComparison.Ordinal);
        var noDigest = atIdx >= 0 ? reference[..atIdx] : reference;
        var slashIdx = noDigest.IndexOf('/', StringComparison.Ordinal);
        string remainder;
        if (slashIdx < 0 || (!noDigest[..slashIdx].Contains('.', StringComparison.Ordinal)
            && !noDigest[..slashIdx].Contains(':', StringComparison.Ordinal)
            && noDigest[..slashIdx] != "localhost"))
        {
            remainder = noDigest;
        }
        else
        {
            remainder = noDigest[(slashIdx + 1)..];
        }
        return $"{targetRegistry}{targetNs}/{remainder}";
    }

    private static string NormalizeReference(string reference)
    {
        var trimmed = reference.Trim();
        var atIdx = trimmed.IndexOf('@', StringComparison.Ordinal);
        var noDigest = atIdx >= 0 ? trimmed[..atIdx] : trimmed;
        var slashIdx = noDigest.IndexOf('/', StringComparison.Ordinal);
        var hasRegistry = slashIdx > 0
            && (noDigest[..slashIdx].Contains('.', StringComparison.Ordinal)
                || noDigest[..slashIdx].Contains(':', StringComparison.Ordinal)
                || noDigest[..slashIdx] == "localhost");
        var afterRegistry = hasRegistry ? noDigest[(slashIdx + 1)..] : noDigest;
        var lastColon = afterRegistry.LastIndexOf(':');
        var hasTag = lastColon >= 0 && !afterRegistry[(lastColon + 1)..].Contains('/', StringComparison.Ordinal);
        return hasTag ? trimmed : trimmed + ":latest";
    }

    private static string SanitizeFilename(string reference)
    {
        var noDigest = reference.Replace('@', '_');
        var safe = SafeFilenameRegex().Replace(noDigest, "_");
        return safe.Trim('_', '.');
    }

    private static string ToolVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static async Task CreateTarGzAsync(string sourceDir, string outputPath, CancellationToken ct)
    {
        await using var fileStream = File.Create(outputPath);
        await using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
        var rootFull = Path.GetFullPath(sourceDir);
        await using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        var outputName = Path.GetFileName(outputPath);
        foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
            if (string.Equals(rel, outputName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var entry = new PaxTarEntry(TarEntryType.RegularFile, rel)
            {
                ModificationTime = DateTimeOffset.UtcNow
            };
            await using var src = File.OpenRead(file);
            entry.DataStream = src;
            await writer.WriteEntryAsync(entry, ct);
        }
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex SafeFilenameRegex();
}
