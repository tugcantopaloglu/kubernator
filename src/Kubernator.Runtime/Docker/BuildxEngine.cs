using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kubernator.Core.Containers;

namespace Kubernator.Runtime.Docker;

public sealed class BuildxEngine : IContainerEngine
{
    private readonly DockerEngine inner;

    public BuildxEngine(DockerEngine inner)
    {
        this.inner = inner;
    }

    public string Kind => "docker-buildx";

    public bool SupportsMultiPlatform => true;

    public Task<EngineInfo> GetInfoAsync(CancellationToken ct = default) =>
        inner.GetInfoAsync(ct);

    public Task<ImageInfo?> GetImageAsync(string reference, CancellationToken ct = default) =>
        inner.GetImageAsync(reference, ct);

    public Task SaveImageAsync(string reference, string outputTarPath, CancellationToken ct = default) =>
        inner.SaveImageAsync(reference, outputTarPath, ct);

    public Task SaveImageAsync(string reference, string platform, string outputTarPath, CancellationToken ct = default) =>
        inner.SaveImageAsync(reference, outputTarPath, ct);

    public IAsyncEnumerable<string> PullImageAsync(string reference, string? platform = null, CancellationToken ct = default) =>
        inner.PullImageAsync(reference, platform, ct);

    public Task LoadImageAsync(string tarPath, CancellationToken ct = default) =>
        inner.LoadImageAsync(tarPath, ct);

    public Task TagImageAsync(string sourceReference, string targetReference, CancellationToken ct = default) =>
        inner.TagImageAsync(sourceReference, targetReference, ct);

    public IAsyncEnumerable<string> PushImageAsync(string reference, CancellationToken ct = default) =>
        inner.PushImageAsync(reference, ct);

    public async IAsyncEnumerable<string> BuildAsync(BuildContext context, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var platform = context.Platforms.Count > 0 ? context.Platforms[0] : null;
        if (platform is null)
        {
            await foreach (var line in inner.BuildAsync(context, ct))
            {
                yield return line;
            }
            yield break;
        }

        var args = BuildArgs(context, platform);
        await foreach (var line in StreamProcessAsync("docker", args, context.ContextDirectory, ct))
        {
            yield return line;
        }
    }

    public static async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await RunAsync("docker", ["buildx", "version"], ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> BuildArgs(BuildContext context, string platform)
    {
        var dockerfileRelative = Path.GetRelativePath(context.ContextDirectory, context.DockerfilePath)
            .Replace('\\', '/');

        var args = new List<string>
        {
            "buildx", "build",
            "--platform", platform,
            "-f", dockerfileRelative,
            "-t", $"{context.ImageName}:{context.ImageTag}",
            "--pull",
            "--load"
        };
        foreach (var tag in context.AdditionalTags)
        {
            args.Add("-t");
            args.Add(tag);
        }
        foreach (var (k, v) in context.BuildArgs)
        {
            args.Add("--build-arg");
            args.Add($"{k}={v}");
        }
        args.Add(".");
        return args;
    }

    private static async Task RunAsync(string fileName, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"failed to start {fileName}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', args)} exited {proc.ExitCode}: {stderrTask.Result.TrimEnd()}");
        }
    }

    private static async IAsyncEnumerable<string> StreamProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"failed to start {fileName}");

        var pumpOut = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) is not null)
            {
                await channel.Writer.WriteAsync(line, ct);
            }
        }, ct);
        var pumpErr = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync(ct)) is not null)
            {
                await channel.Writer.WriteAsync(line, ct);
            }
        }, ct);

        var waiter = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(pumpOut, pumpErr);
                await proc.WaitForExitAsync(ct);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        await foreach (var msg in channel.Reader.ReadAllAsync(ct))
        {
            yield return msg;
        }

        await waiter;
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', args)} exited {proc.ExitCode}");
        }
    }
}
