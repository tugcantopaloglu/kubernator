using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Kubernator.Core.ClusterProvisioning.Distros;

namespace Kubernator.Core.ClusterProvisioning.Artifacts;

public sealed class ClusterArtifactBundleService : IClusterArtifactBundleService
{
    private readonly HttpClient http;

    public ClusterArtifactBundleService(HttpClient http)
    {
        this.http = http;
    }

    internal sealed record DownloadItem
    {
        public required string Url { get; init; }
        public required string RelativePath { get; init; }
        public required string Kind { get; init; }
        public string? Arch { get; init; }
        public bool Required { get; init; } = true;
        public bool Executable { get; init; }
        public string? ChecksumUrl { get; init; }
    }

    public async Task<ClusterArtifactManifest> PullAsync(ClusterArtifactPullOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (options.Architectures.Count == 0)
        {
            throw new ArgumentException("at least one architecture is required", nameof(options));
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var plan = BuildDownloadPlan(options);
        var entries = new List<ClusterArtifactEntry>();

        foreach (var item in plan)
        {
            var destination = Path.Combine(options.OutputDirectory, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            progress?.Report($"downloading {item.Url}");
            string sha256;
            try
            {
                sha256 = await DownloadAsync(item.Url, destination, ct);
            }
            catch (Exception ex) when (!item.Required)
            {
                progress?.Report($"skipping optional artifact {item.RelativePath}: {ex.Message}");
                continue;
            }

            if (item.ChecksumUrl is not null)
            {
                await VerifyChecksumAsync(item.ChecksumUrl, Path.GetFileName(destination), sha256, progress, ct);
            }

            if (item.Executable && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            entries.Add(new ClusterArtifactEntry
            {
                Kind = item.Kind,
                Arch = item.Arch,
                RelativePath = item.RelativePath,
                SizeBytes = new FileInfo(destination).Length,
                Sha256 = sha256
            });
        }

        return new ClusterArtifactManifest
        {
            Distro = options.Distro.ToString().ToLowerInvariant(),
            Version = options.Version,
            Entries = entries
        };
    }

    public async Task<string> PackAsync(string bundleDirectory, string archivePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var rootFull = Path.GetFullPath(bundleDirectory);
        if (!Directory.Exists(rootFull))
        {
            throw new DirectoryNotFoundException($"bundle directory not found: {rootFull}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);
        progress?.Report($"packing {rootFull} -> {archivePath}");

        await using (var fileStream = File.Create(archivePath))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            var archiveFull = Path.GetFullPath(archivePath);
            foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFullPath(file), archiveFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
                var entry = new PaxTarEntry(TarEntryType.RegularFile, rel)
                {
                    ModificationTime = DateTimeOffset.UtcNow
                };
                await using var src = File.OpenRead(file);
                entry.DataStream = src;
                await writer.WriteEntryAsync(entry, ct);
            }
        }

        progress?.Report($"packed {archivePath}");
        return archivePath;
    }

    internal static IReadOnlyList<DownloadItem> BuildDownloadPlan(ClusterArtifactPullOptions options)
    {
        var items = new List<DownloadItem>();
        var kubeVersion = options.Version.Split('+', 2)[0];

        switch (options.Distro)
        {
            case DistroKind.Rke2:
                BuildRke2Plan(options, items);
                break;
            case DistroKind.K3s:
                BuildK3sPlan(options, items);
                break;
            default:
                throw new NotSupportedException($"pulling artifacts for distro '{options.Distro}' is not supported");
        }

        foreach (var arch in options.Architectures)
        {
            if (options.IncludeKubectl)
            {
                items.Add(new DownloadItem
                {
                    Url = $"https://dl.k8s.io/release/{kubeVersion}/bin/linux/{arch}/kubectl",
                    RelativePath = $"tools/kubectl-{arch}",
                    Kind = "kubectl",
                    Arch = arch,
                    Executable = true
                });
            }

            if (options.IncludeHelm)
            {
                items.Add(new DownloadItem
                {
                    Url = $"https://get.helm.sh/helm-{options.HelmVersion}-linux-{arch}.tar.gz",
                    RelativePath = $"tools/helm-{options.HelmVersion}-linux-{arch}.tar.gz",
                    Kind = "helm",
                    Arch = arch
                });
            }

            if (options.IncludeK9s)
            {
                items.Add(new DownloadItem
                {
                    Url = $"https://github.com/derailed/k9s/releases/download/{options.K9sVersion}/k9s_Linux_{arch}.tar.gz",
                    RelativePath = $"tools/k9s-{arch}-{options.K9sVersion}.tar.gz",
                    Kind = "k9s",
                    Arch = arch
                });
            }
        }

        return items;
    }

    private static void BuildRke2Plan(ClusterArtifactPullOptions options, List<DownloadItem> items)
    {
        const string baseUrl = "https://github.com/rancher/rke2/releases/download";
        var releaseBase = $"{baseUrl}/{options.Version}";

        items.Add(new DownloadItem
        {
            Url = "https://get.rke2.io",
            RelativePath = "install.sh",
            Kind = "install-script",
            Executable = true
        });

        foreach (var arch in options.Architectures)
        {
            var checksumUrl = $"{releaseBase}/sha256sum-{arch}.txt";
            items.Add(new DownloadItem
            {
                Url = $"{releaseBase}/rke2.linux-{arch}.tar.gz",
                RelativePath = $"artifacts/{arch}/rke2.linux-{arch}.tar.gz",
                Kind = "rke2-artifact",
                Arch = arch,
                ChecksumUrl = checksumUrl
            });
            items.Add(new DownloadItem
            {
                Url = $"{releaseBase}/rke2-images.linux-{arch}.tar.zst",
                RelativePath = $"artifacts/{arch}/rke2-images.linux-{arch}.tar.zst",
                Kind = "rke2-images",
                Arch = arch,
                ChecksumUrl = checksumUrl
            });

            if (options.IncludeSelinuxPolicy)
            {
                var selinuxVersion = options.SelinuxPolicyVersion ?? options.Version;
                items.Add(new DownloadItem
                {
                    Url = $"https://github.com/rancher/rke2-selinux/releases/download/{selinuxVersion}/rke2-selinux.noarch.rpm",
                    RelativePath = $"selinux/{arch}/rke2-selinux.rpm",
                    Kind = "selinux-policy",
                    Arch = arch,
                    Required = false
                });
            }
        }
    }

    private static void BuildK3sPlan(ClusterArtifactPullOptions options, List<DownloadItem> items)
    {
        const string baseUrl = "https://github.com/k3s-io/k3s/releases/download";
        var releaseBase = $"{baseUrl}/{options.Version}";

        items.Add(new DownloadItem
        {
            Url = "https://get.k3s.io",
            RelativePath = "install.sh",
            Kind = "install-script",
            Executable = true
        });

        foreach (var arch in options.Architectures)
        {
            var binaryFileName = arch == "arm64" ? "k3s-arm64" : "k3s";
            var checksumUrl = $"{releaseBase}/sha256sum-{arch}.txt";
            items.Add(new DownloadItem
            {
                Url = $"{releaseBase}/{binaryFileName}",
                RelativePath = $"artifacts/{arch}/{binaryFileName}",
                Kind = "k3s-binary",
                Arch = arch,
                Executable = true,
                ChecksumUrl = checksumUrl
            });
            items.Add(new DownloadItem
            {
                Url = $"{releaseBase}/k3s-airgap-images-{arch}.tar.zst",
                RelativePath = $"artifacts/{arch}/k3s-airgap-images-{arch}.tar.zst",
                Kind = "k3s-images",
                Arch = arch,
                ChecksumUrl = checksumUrl
            });

            if (options.IncludeSelinuxPolicy)
            {
                var selinuxVersion = options.SelinuxPolicyVersion ?? options.Version;
                items.Add(new DownloadItem
                {
                    Url = $"https://github.com/k3s-io/k3s-selinux/releases/download/{selinuxVersion}/k3s-selinux.noarch.rpm",
                    RelativePath = $"selinux/{arch}/k3s-selinux.rpm",
                    Kind = "selinux-policy",
                    Arch = arch,
                    Required = false
                });
            }
        }
    }

    private async Task<string> DownloadAsync(string url, string destinationPath, CancellationToken ct)
    {
        Stream input;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "file")
        {
            input = File.OpenRead(uri.LocalPath);
        }
        else
        {
            var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            input = await resp.Content.ReadAsStreamAsync(ct);
        }

        await using (input)
        await using (var output = File.Create(destinationPath))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
    }

    private async Task VerifyChecksumAsync(string checksumUrl, string fileName, string actualSha256, IProgress<string>? progress, CancellationToken ct)
    {
        string text;
        try
        {
            if (Uri.TryCreate(checksumUrl, UriKind.Absolute, out var uri) && uri.Scheme == "file")
            {
                text = await File.ReadAllTextAsync(uri.LocalPath, ct);
            }
            else
            {
                var resp = await http.GetAsync(checksumUrl, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    progress?.Report($"checksum manifest unavailable at {checksumUrl}, skipping verification for {fileName}");
                    return;
                }
                text = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"could not fetch checksum manifest at {checksumUrl}: {ex.Message}");
            return;
        }

        var expected = ParseChecksum(text, fileName);
        if (expected is null)
        {
            progress?.Report($"no checksum entry for {fileName} in {checksumUrl}, skipping verification");
            return;
        }

        if (!string.Equals(expected, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"sha256 mismatch for {fileName}: expected {expected}, got {actualSha256}");
        }
    }

    private static string? ParseChecksum(string checksumFileText, string fileName)
    {
        foreach (var line in checksumFileText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }
            var name = parts[1].TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.Ordinal))
            {
                return parts[0];
            }
        }
        return null;
    }
}
