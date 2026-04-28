using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kubernator.Core.Containers;
using Kubernator.Core.Generation;
using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Packaging.Hashing;
using Kubernator.Core.Packaging.Sbom;
using Kubernator.Core.Packaging.Scripts;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Packaging;

public sealed class BundleService : IBundleService
{
    private const string SchemaVersion = "1.0";
    private const string ManifestFileName = "manifest.json";
    private const string ChecksumFileName = "manifest.sha256";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<BundleResult> CreateAsync(
        BuildPlan plan,
        BundleOptions options,
        IContainerEngine engine,
        CancellationToken ct = default)
    {
        if (Directory.Exists(options.ScratchDirectory))
        {
            Directory.Delete(options.ScratchDirectory, recursive: true);
        }
        Directory.CreateDirectory(options.ScratchDirectory);

        var imagesDir = Path.Combine(options.ScratchDirectory, "images");
        var manifestsDir = Path.Combine(options.ScratchDirectory, "manifests");
        var scriptsDir = Path.Combine(options.ScratchDirectory, "scripts");
        var sbomDir = Path.Combine(options.ScratchDirectory, "sbom");
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(manifestsDir);
        Directory.CreateDirectory(scriptsDir);
        if (options.IncludeSbom)
        {
            Directory.CreateDirectory(sbomDir);
        }

        await EnsureImageBuiltAsync(plan, options, engine, ct);

        var imageRef = plan.FullImageReference;
        var imageInfo = await engine.GetImageAsync(imageRef, ct)
            ?? throw new InvalidOperationException($"image not found after build: {imageRef}");

        var imageTarName = $"{SanitizeFilename(plan.ImageName)}-{plan.ImageTag}.tar";
        var imageTarPath = Path.Combine(imagesDir, imageTarName);
        await engine.SaveImageAsync(imageRef, imageTarPath, ct);

        var imageHash = await Sha256Hasher.HashFileAsync(imageTarPath, ct);
        var imageEntry = new ImageEntry
        {
            Reference = imageRef,
            TarRelativePath = $"images/{imageTarName}",
            SizeBytes = new FileInfo(imageTarPath).Length,
            Sha256 = imageHash,
            ImageId = imageInfo.Id
        };

        var ns = options.KubernetesNamespace ?? "default";
        var k8sName = SanitizeKubernetesName(plan.ImageName);
        var manifestFiles = WriteKubernetesManifests(plan, manifestsDir, k8sName, ns, options);

        if (options.IncludeSbom)
        {
            await File.WriteAllTextAsync(
                Path.Combine(sbomDir, $"{k8sName}.cyclonedx.json"),
                CycloneDxBuilder.Build(plan.App, ToolVersion()),
                ct);
            await File.WriteAllTextAsync(
                Path.Combine(sbomDir, $"{k8sName}.spdx.json"),
                SpdxBuilder.Build(plan.App, ToolVersion()),
                ct);
        }

        await File.WriteAllTextAsync(Path.Combine(scriptsDir, "install.sh"), BundleScripts.InstallSh, ct);
        await File.WriteAllTextAsync(Path.Combine(scriptsDir, "install.ps1"), BundleScripts.InstallPs1, ct);
        await File.WriteAllTextAsync(Path.Combine(scriptsDir, "verify.sh"), BundleScripts.VerifySh, ct);
        await File.WriteAllTextAsync(Path.Combine(scriptsDir, "verify.ps1"), BundleScripts.VerifyPs1, ct);

        var topInstallSh = Path.Combine(options.ScratchDirectory, "install.sh");
        var topInstallPs1 = Path.Combine(options.ScratchDirectory, "install.ps1");
        await File.WriteAllTextAsync(topInstallSh, BundleScripts.InstallSh, ct);
        await File.WriteAllTextAsync(topInstallPs1, BundleScripts.InstallPs1, ct);

        var fileEntries = new List<FileEntry>();
        await CollectFileEntriesAsync(options.ScratchDirectory, fileEntries, ct);

        var manifest = new BundleManifest
        {
            SchemaVersion = SchemaVersion,
            Tool = "kubernator",
            ToolVersion = ToolVersion(),
            CreatedAt = DateTimeOffset.UtcNow,
            App = new AppInfo
            {
                Name = plan.ImageName,
                Version = plan.ImageTag,
                Kind = plan.App.Kind.ToString(),
                Flavor = plan.App.Flavor.ToString(),
                TargetOs = plan.App.Runtime.TargetOs.ToString(),
                TargetArch = plan.App.Runtime.TargetArch.ToString(),
                Tfm = plan.App.Runtime.Tfm ?? string.Empty
            },
            Images = [imageEntry],
            Files = fileEntries,
            KubernetesNamespace = ns,
            Notes = plan.Notes
        };

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var manifestPath = Path.Combine(options.ScratchDirectory, ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        var checksumLines = new List<string>();
        var imageRelativePath = imageEntry.TarRelativePath;
        checksumLines.Add(Sha256Hasher.FormatChecksumLine(imageHash, imageRelativePath));
        foreach (var entry in fileEntries.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            checksumLines.Add(Sha256Hasher.FormatChecksumLine(entry.Sha256, entry.RelativePath));
        }
        var manifestHash = Sha256Hasher.HashBytes(Encoding.UTF8.GetBytes(manifestJson));
        checksumLines.Add(Sha256Hasher.FormatChecksumLine(manifestHash, ManifestFileName));

        var checksumPath = Path.Combine(options.ScratchDirectory, ChecksumFileName);
        await File.WriteAllTextAsync(checksumPath, string.Join('\n', checksumLines) + "\n", ct);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputBundlePath)!);
        await CreateTarGzAsync(options.ScratchDirectory, options.OutputBundlePath, ct);

        var bundleHash = await Sha256Hasher.HashFileAsync(options.OutputBundlePath, ct);

        if (!options.KeepScratch)
        {
            try
            {
                Directory.Delete(options.ScratchDirectory, recursive: true);
            }
            catch
            {
            }
        }

        return new BundleResult
        {
            BundlePath = options.OutputBundlePath,
            BundleSizeBytes = new FileInfo(options.OutputBundlePath).Length,
            BundleSha256 = bundleHash,
            Manifest = manifest
        };
    }

    public async Task<BundleVerificationResult> VerifyAsync(string bundlePath, CancellationToken ct = default)
    {
        if (!File.Exists(bundlePath))
        {
            return new BundleVerificationResult { Ok = false, Errors = [$"bundle not found: {bundlePath}"], Manifest = null };
        }

        var temp = Path.Combine(Path.GetTempPath(), "kubernator-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await ExtractTarGzAsync(bundlePath, temp, ct);

            var manifestPath = Path.Combine(temp, ManifestFileName);
            var checksumPath = Path.Combine(temp, ChecksumFileName);
            if (!File.Exists(manifestPath) || !File.Exists(checksumPath))
            {
                return new BundleVerificationResult
                {
                    Ok = false,
                    Errors = ["bundle is missing manifest.json or manifest.sha256"],
                    Manifest = null
                };
            }

            var manifestText = await File.ReadAllTextAsync(manifestPath, ct);
            var manifest = JsonSerializer.Deserialize<BundleManifest>(manifestText, JsonOptions);
            if (manifest is null)
            {
                return new BundleVerificationResult
                {
                    Ok = false,
                    Errors = ["bundle manifest could not be parsed"],
                    Manifest = null
                };
            }

            var errors = new List<string>();
            var lines = await File.ReadAllLinesAsync(checksumPath, ct);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                var (expected, relative) = Sha256Hasher.ParseChecksumLine(line);
                var fullPath = Path.Combine(temp, relative);
                if (!File.Exists(fullPath))
                {
                    errors.Add($"missing file: {relative}");
                    continue;
                }
                var actual = await Sha256Hasher.HashFileAsync(fullPath, ct);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    errors.Add($"hash mismatch: {relative}");
                }
            }

            return new BundleVerificationResult
            {
                Ok = errors.Count == 0,
                Errors = errors,
                Manifest = manifest
            };
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task EnsureImageBuiltAsync(
        BuildPlan plan,
        BundleOptions options,
        IContainerEngine engine,
        CancellationToken ct)
    {
        var existing = await engine.GetImageAsync(plan.FullImageReference, ct);
        if (existing is not null)
        {
            return;
        }
        if (!options.BuildIfMissing)
        {
            throw new InvalidOperationException($"image {plan.FullImageReference} not present and BuildIfMissing is false");
        }

        var contextDir = Path.Combine(options.ScratchDirectory, "build-context");
        Directory.CreateDirectory(contextDir);

        var scratchAbsolute = Path.GetFullPath(options.ScratchDirectory);
        var sourceAbsolute = Path.GetFullPath(plan.App.SourcePath);

        foreach (var dir in Directory.EnumerateDirectories(plan.App.SourcePath, "*", SearchOption.AllDirectories))
        {
            if (IsUnderneath(Path.GetFullPath(dir), scratchAbsolute))
            {
                continue;
            }
            var rel = Path.GetRelativePath(sourceAbsolute, dir);
            Directory.CreateDirectory(Path.Combine(contextDir, rel));
        }
        foreach (var file in Directory.EnumerateFiles(plan.App.SourcePath, "*", SearchOption.AllDirectories))
        {
            if (IsUnderneath(Path.GetFullPath(file), scratchAbsolute))
            {
                continue;
            }
            var rel = Path.GetRelativePath(sourceAbsolute, file);
            var target = Path.Combine(contextDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        var dockerfilePath = Path.Combine(contextDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileEmitter.Emit(plan), ct);
        await File.WriteAllTextAsync(Path.Combine(contextDir, ".dockerignore"), DockerignoreEmitter.Emit(), ct);

        var buildCtx = new BuildContext
        {
            ContextDirectory = contextDir,
            DockerfilePath = dockerfilePath,
            ImageName = plan.ImageName,
            ImageTag = plan.ImageTag
        };
        await foreach (var _ in engine.BuildAsync(buildCtx, ct))
        {
        }
    }

    private static List<string> WriteKubernetesManifests(
        BuildPlan plan,
        string manifestsDir,
        string name,
        string ns,
        BundleOptions options)
    {
        var written = new List<string>();
        var genOptions = new GenerationOptions
        {
            OutputDirectory = manifestsDir,
            Namespace = ns,
            Replicas = options.Replicas
        };

        var deployment = KubernetesEmitter.Deployment(plan, genOptions, name, ns);
        var deploymentPath = Path.Combine(manifestsDir, "deployment.yaml");
        File.WriteAllText(deploymentPath, deployment);
        written.Add(deploymentPath);

        if (plan.ExposedPorts.Count > 0)
        {
            var service = KubernetesEmitter.Service(plan, name, ns);
            var servicePath = Path.Combine(manifestsDir, "service.yaml");
            File.WriteAllText(servicePath, service);
            written.Add(servicePath);
        }

        var policy = KubernetesEmitter.NetworkPolicy(plan, name, ns);
        var policyPath = Path.Combine(manifestsDir, "networkpolicy.yaml");
        File.WriteAllText(policyPath, policy);
        written.Add(policyPath);

        return written;
    }

    private static async Task CollectFileEntriesAsync(
        string root,
        List<FileEntry> entries,
        CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("build-context/", StringComparison.Ordinal))
            {
                continue;
            }
            if (relative is ManifestFileName or ChecksumFileName)
            {
                continue;
            }
            if (relative.StartsWith("images/", StringComparison.Ordinal))
            {
                continue;
            }
            var hash = await Sha256Hasher.HashFileAsync(file, ct);
            entries.Add(new FileEntry
            {
                RelativePath = relative,
                SizeBytes = new FileInfo(file).Length,
                Sha256 = hash
            });
        }
    }

    private static async Task CreateTarGzAsync(string sourceDir, string outputPath, CancellationToken ct)
    {
        await using var fileStream = File.Create(outputPath);
        await using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
        await TarFile.CreateFromDirectoryAsync(sourceDir, gzip, includeBaseDirectory: false, ct);
    }

    private static async Task ExtractTarGzAsync(string bundlePath, string targetDir, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(bundlePath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, targetDir, overwriteFiles: true, ct);
    }

    private static string ToolVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsUnderneath(string candidate, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFilename(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars);
    }

    private static string SanitizeKubernetesName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name = new string(chars).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }
        return string.IsNullOrEmpty(name) ? "app" : name;
    }
}
