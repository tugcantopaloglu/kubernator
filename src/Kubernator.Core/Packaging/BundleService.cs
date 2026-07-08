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
using Kubernator.Core.Tls;

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
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"preparing scratch at {options.ScratchDirectory}");
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

        var platforms = plan.Platforms.Count > 0 ? plan.Platforms : [string.Empty];
        if (platforms.Count > 1 && !engine.SupportsMultiPlatform)
        {
            throw new InvalidOperationException(
                $"Multi-platform bundle requested ({string.Join(", ", platforms)}) but engine '{engine.Kind}' does not support it.");
        }

        var imageEntries = new List<ImageEntry>();
        foreach (var platform in platforms)
        {
            var label = string.IsNullOrEmpty(platform) ? "(host)" : platform;
            progress?.Report($"resolving image {plan.FullImageReference} for {label}");
            await EnsureImageBuiltAsync(plan, options, engine, platform, progress, ct);

            var imageRef = plan.FullImageReference;
            var imageInfo = await engine.GetImageAsync(imageRef, ct)
                ?? throw new InvalidOperationException($"image not found after build: {imageRef}");

            var archSuffix = string.IsNullOrEmpty(platform) ? "" : "-" + ArchSlug(platform);
            var imageTarName = $"{SanitizeFilename(plan.ImageName)}-{plan.ImageTag}{archSuffix}.tar";
            var imageTarPath = Path.Combine(imagesDir, imageTarName);
            progress?.Report($"saving image to {imageTarName}");
            if (string.IsNullOrEmpty(platform))
            {
                await engine.SaveImageAsync(imageRef, imageTarPath, ct);
            }
            else
            {
                await engine.SaveImageAsync(imageRef, platform, imageTarPath, ct);
            }

            progress?.Report($"hashing {imageTarName}");
            var imageHash = await Sha256Hasher.HashFileAsync(imageTarPath, ct);
            imageEntries.Add(new ImageEntry
            {
                Reference = imageRef,
                TarRelativePath = $"images/{imageTarName}",
                SizeBytes = new FileInfo(imageTarPath).Length,
                Sha256 = imageHash,
                ImageId = imageInfo.Id,
                Platform = string.IsNullOrEmpty(platform) ? null : platform
            });
        }

        var ns = options.KubernetesNamespace ?? "default";
        var k8sName = SanitizeKubernetesName(plan.ImageName);
        progress?.Report("writing kubernetes manifests");
        var manifestFiles = WriteKubernetesManifests(plan, manifestsDir, k8sName, ns, options);

        if (options.IncludeSbom)
        {
            progress?.Report("writing SBOM (CycloneDX + SPDX)");
            await File.WriteAllTextAsync(
                Path.Combine(sbomDir, $"{k8sName}.cyclonedx.json"),
                CycloneDxBuilder.Build(plan.App, ToolVersion(), options.SourceDateEpoch),
                ct);
            await File.WriteAllTextAsync(
                Path.Combine(sbomDir, $"{k8sName}.spdx.json"),
                SpdxBuilder.Build(plan.App, ToolVersion(), options.SourceDateEpoch),
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

        progress?.Report("hashing bundle files");
        var fileEntries = new List<FileEntry>();
        await CollectFileEntriesAsync(options.ScratchDirectory, fileEntries, ct);

        var manifest = new BundleManifest
        {
            SchemaVersion = SchemaVersion,
            Tool = "kubernator",
            ToolVersion = ToolVersion(),
            CreatedAt = options.SourceDateEpoch ?? DateTimeOffset.UtcNow,
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
            Images = imageEntries,
            Files = fileEntries,
            KubernetesNamespace = ns,
            Notes = plan.Notes
        };

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var manifestPath = Path.Combine(options.ScratchDirectory, ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        var checksumLines = new List<string>();
        foreach (var entry in imageEntries.OrderBy(e => e.TarRelativePath, StringComparer.Ordinal))
        {
            checksumLines.Add(Sha256Hasher.FormatChecksumLine(entry.Sha256, entry.TarRelativePath));
        }
        foreach (var entry in fileEntries.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            checksumLines.Add(Sha256Hasher.FormatChecksumLine(entry.Sha256, entry.RelativePath));
        }
        var manifestHash = Sha256Hasher.HashBytes(Encoding.UTF8.GetBytes(manifestJson));
        checksumLines.Add(Sha256Hasher.FormatChecksumLine(manifestHash, ManifestFileName));

        var checksumPath = Path.Combine(options.ScratchDirectory, ChecksumFileName);
        await File.WriteAllTextAsync(checksumPath, string.Join('\n', checksumLines) + "\n", ct);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputBundlePath)!);
        progress?.Report($"compressing bundle to {Path.GetFileName(options.OutputBundlePath)}");
        await CreateTarGzAsync(options.ScratchDirectory, options.OutputBundlePath, options.Compression, options.SourceDateEpoch, ct);
        if (options.SourceDateEpoch is { } epoch)
        {
            ZeroGzipHeaderMtime(options.OutputBundlePath, epoch);
        }

        progress?.Report("computing bundle hash");
        var bundleHash = await Sha256Hasher.HashFileAsync(options.OutputBundlePath, ct);
        await File.WriteAllTextAsync(
            options.OutputBundlePath + ".sha256",
            $"{bundleHash}  {Path.GetFileName(options.OutputBundlePath)}\n",
            ct);

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

    public async Task<BundleVerificationResult> VerifyAsync(
        string bundlePath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(bundlePath))
        {
            return new BundleVerificationResult { Ok = false, Errors = [$"bundle not found: {bundlePath}"], Manifest = null };
        }

        var outerSidecar = bundlePath + ".sha256";
        if (File.Exists(outerSidecar))
        {
            progress?.Report("verifying outer bundle hash");
            var sidecarText = (await File.ReadAllTextAsync(outerSidecar, ct)).Trim();
            var (expected, _) = Sha256Hasher.ParseChecksumLine(sidecarText);
            var actual = await Sha256Hasher.HashFileAsync(bundlePath, ct);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return new BundleVerificationResult
                {
                    Ok = false,
                    Errors = [$"outer bundle hash mismatch (expected {expected}, got {actual})"],
                    Manifest = null
                };
            }
        }

        var temp = Path.Combine(Path.GetTempPath(), "kubernator-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            progress?.Report($"extracting {Path.GetFileName(bundlePath)}");
            try
            {
                await ExtractTarGzAsync(bundlePath, temp, ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or FormatException or IOException)
            {
                return new BundleVerificationResult
                {
                    Ok = false,
                    Errors = [$"bundle archive is corrupt: {ex.Message}"],
                    Manifest = null
                };
            }

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

            progress?.Report("verifying file hashes");
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            var lines = await File.ReadAllLinesAsync(checksumPath, ct);
            var work = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(Sha256Hasher.ParseChecksumLine)
                .ToArray();

            await Parallel.ForEachAsync(
                work,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
                async (item, token) =>
                {
                    var (expected, relative) = item;
                    var fullPath = Path.Combine(temp, relative);
                    if (!File.Exists(fullPath))
                    {
                        errors.Add($"missing file: {relative}");
                        return;
                    }
                    var actual = await Sha256Hasher.HashFileAsync(fullPath, token);
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        errors.Add($"hash mismatch: {relative}");
                    }
                });

            return new BundleVerificationResult
            {
                Ok = errors.IsEmpty,
                Errors = errors.ToArray(),
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
        string platform,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var isMultiPlatform = !string.IsNullOrEmpty(platform) && plan.Platforms.Count > 1;
        if (!isMultiPlatform)
        {
            var existing = await engine.GetImageAsync(plan.FullImageReference, ct);
            if (existing is not null)
            {
                progress?.Report("image already present in engine");
                return;
            }
        }
        if (!options.BuildIfMissing)
        {
            throw new InvalidOperationException($"image {plan.FullImageReference} not present and BuildIfMissing is false");
        }
        progress?.Report(isMultiPlatform
            ? $"building image for {platform}"
            : "building image (no cached layer hit)");

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
            HardLinkOrCopy(file, target);
        }

        var dockerfilePath = Path.Combine(contextDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileEmitter.Emit(plan), ct);
        await File.WriteAllTextAsync(Path.Combine(contextDir, ".dockerignore"), DockerignoreEmitter.Emit(), ct);

        var buildCtx = new BuildContext
        {
            ContextDirectory = contextDir,
            DockerfilePath = dockerfilePath,
            ImageName = plan.ImageName,
            ImageTag = plan.ImageTag,
            Platforms = string.IsNullOrEmpty(platform) ? [] : [platform]
        };
        await foreach (var _ in engine.BuildAsync(buildCtx, ct))
        {
        }
    }

    private static string ArchSlug(string platform)
    {
        var arch = platform.Contains('/', StringComparison.Ordinal)
            ? platform[(platform.IndexOf('/', StringComparison.Ordinal) + 1)..]
            : platform;
        return arch.Replace('/', '-');
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

        if (plan.Exposure is not null)
        {
            var ingressPath = Path.Combine(manifestsDir, "ingress.yaml");
            File.WriteAllText(ingressPath, IngressEmitter.Ingress(plan, name, ns));
            written.Add(ingressPath);

            switch (plan.Exposure.TlsMode)
            {
                case TlsMode.SelfSigned:
                    {
                        var material = SelfSignedCertificateGenerator.Generate(
                            plan.Exposure.PrimaryHostname,
                            plan.Exposure.AdditionalHostnames);
                        var secretPath = Path.Combine(manifestsDir, "tls-secret.yaml");
                        File.WriteAllText(secretPath, IngressEmitter.TlsSecret(plan.Exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem));
                        written.Add(secretPath);
                        break;
                    }
                case TlsMode.UserProvided:
                    {
                        if (string.IsNullOrEmpty(plan.Exposure.UserCertificatePemPath) ||
                            string.IsNullOrEmpty(plan.Exposure.UserPrivateKeyPemPath))
                        {
                            throw new InvalidOperationException(
                                "TlsMode.UserProvided requires UserCertificatePemPath and UserPrivateKeyPemPath");
                        }
                        var material = SelfSignedCertificateGenerator.LoadFromFiles(
                            plan.Exposure.UserCertificatePemPath,
                            plan.Exposure.UserPrivateKeyPemPath);
                        var secretPath = Path.Combine(manifestsDir, "tls-secret.yaml");
                        File.WriteAllText(secretPath, IngressEmitter.TlsSecret(plan.Exposure.TlsSecretName, ns, material.CertificatePem, material.PrivateKeyPem));
                        written.Add(secretPath);
                        break;
                    }
                case TlsMode.CertManager:
                    {
                        var certPath = Path.Combine(manifestsDir, "certificate.yaml");
                        File.WriteAllText(certPath, IngressEmitter.CertManagerCertificate(plan, name, ns));
                        written.Add(certPath);
                        break;
                    }
            }
        }

        if (options.Scaling is { } scaling)
        {
            if (scaling.HpaEnabled)
            {
                var hpaPath = Path.Combine(manifestsDir, "hpa.yaml");
                File.WriteAllText(hpaPath, AutoscalingEmitter.Hpa(name, ns, scaling));
                written.Add(hpaPath);
            }
            if (scaling.PdbEnabled)
            {
                var pdbPath = Path.Combine(manifestsDir, "pdb.yaml");
                File.WriteAllText(pdbPath, AutoscalingEmitter.Pdb(name, ns, scaling));
                written.Add(pdbPath);
            }
        }

        return written;
    }

    private static async Task CollectFileEntriesAsync(
        string root,
        List<FileEntry> entries,
        CancellationToken ct)
    {
        var candidates = new List<string>();
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
            candidates.Add(file);
        }

        var hashed = new FileEntry[candidates.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (i, token) =>
            {
                var file = candidates[i];
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var hash = await Sha256Hasher.HashFileAsync(file, token);
                hashed[i] = new FileEntry
                {
                    RelativePath = relative,
                    SizeBytes = new FileInfo(file).Length,
                    Sha256 = hash
                };
            });

        entries.AddRange(hashed);
    }

    private static async Task CreateTarGzAsync(
        string sourceDir,
        string outputPath,
        CompressionLevel compression,
        DateTimeOffset? sourceDateEpoch,
        CancellationToken ct)
    {
        await using var fileStream = File.Create(outputPath);
        await using var gzip = new GZipStream(fileStream, compression);
        if (sourceDateEpoch is null)
        {
            await TarFile.CreateFromDirectoryAsync(sourceDir, gzip, includeBaseDirectory: false, ct);
            return;
        }

        var epoch = sourceDateEpoch.Value;
        await using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);

        var rootFull = Path.GetFullPath(sourceDir);
        var entries = new List<(string Relative, string FullPath, bool IsDirectory)>();
        foreach (var dir in Directory.EnumerateDirectories(rootFull, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootFull, dir).Replace('\\', '/');
            entries.Add((rel + "/", dir, true));
        }
        foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
            entries.Add((rel, file, false));
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));

        foreach (var (relative, full, isDirectory) in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (isDirectory)
            {
                var dirEntry = new PaxTarEntry(TarEntryType.Directory, relative)
                {
                    ModificationTime = epoch,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                };
                await writer.WriteEntryAsync(dirEntry, ct);
            }
            else
            {
                var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, relative)
                {
                    ModificationTime = epoch,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.GroupRead
                        | UnixFileMode.OtherRead
                };
                await using var src = File.OpenRead(full);
                fileEntry.DataStream = src;
                await writer.WriteEntryAsync(fileEntry, ct);
            }
        }
    }

    private static async Task ExtractTarGzAsync(string bundlePath, string targetDir, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(bundlePath);
        await using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: true))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, targetDir, overwriteFiles: true, ct);
            await gzip.CopyToAsync(Stream.Null, ct);
        }

        var trailing = new byte[1];
        var read = await fileStream.ReadAsync(trailing.AsMemory(), ct);
        if (read > 0)
        {
            var remaining = fileStream.Length - fileStream.Position + read;
            throw new InvalidDataException(
                $"trailing data after gzip stream ({remaining} byte(s) at offset {fileStream.Position - read})");
        }
    }

    private static string ToolVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void ZeroGzipHeaderMtime(string path, DateTimeOffset epoch)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (fs.Length < 8)
        {
            return;
        }
        Span<byte> magic = stackalloc byte[2];
        if (fs.Read(magic) != 2 || magic[0] != 0x1F || magic[1] != 0x8B)
        {
            return;
        }
        fs.Seek(4, SeekOrigin.Begin);
        var seconds = (uint)Math.Max(0, epoch.ToUnixTimeSeconds());
        Span<byte> mtime = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(mtime, seconds);
        fs.Write(mtime);
    }

    private static void HardLinkOrCopy(string source, string target)
    {
        if (File.Exists(target))
        {
            File.Delete(target);
        }
        try
        {
            File.CreateSymbolicLink(target, source);
            return;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        File.Copy(source, target, overwrite: true);
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
