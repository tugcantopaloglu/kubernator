using System.Runtime.CompilerServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using Kubernator.Core.Containers;

namespace Kubernator.Runtime.Docker;

public sealed class DockerEngine : IContainerEngine, IDisposable
{
    private readonly DockerClient client;

    public DockerEngine(DockerClient client)
    {
        this.client = client;
    }

    public string Kind => "docker";

    public async Task<EngineInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var version = await client.System.GetVersionAsync(ct);
        var info = await client.System.GetSystemInfoAsync(ct);
        return new EngineInfo
        {
            Name = "docker",
            Version = version.Version ?? "unknown",
            ApiVersion = version.APIVersion ?? "unknown",
            OperatingSystem = info.OperatingSystem ?? "unknown",
            Architecture = info.Architecture ?? "unknown"
        };
    }

    public async IAsyncEnumerable<string> BuildAsync(
        BuildContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(context.ContextDirectory))
        {
            throw new DirectoryNotFoundException(context.ContextDirectory);
        }
        if (!File.Exists(context.DockerfilePath))
        {
            throw new FileNotFoundException(context.DockerfilePath);
        }

        var tarPath = Path.GetTempFileName();
        try
        {
            await TarStream.CreateAsync(context.ContextDirectory, tarPath, ct);
            await using var tarStream = File.OpenRead(tarPath);

            var dockerfileRelative = Path.GetRelativePath(context.ContextDirectory, context.DockerfilePath)
                .Replace('\\', '/');

            var allTags = new List<string>(1 + context.AdditionalTags.Count)
            {
                $"{context.ImageName}:{context.ImageTag}"
            };
            allTags.AddRange(context.AdditionalTags);

            var parameters = new ImageBuildParameters
            {
                Dockerfile = dockerfileRelative,
                Tags = allTags,
                BuildArgs = new Dictionary<string, string>(context.BuildArgs, StringComparer.Ordinal),
                Pull = "true",
                Remove = true,
                ForceRemove = true
            };

            var queue = new System.Threading.Channels.Channel<string>[1];
            queue[0] = System.Threading.Channels.Channel.CreateUnbounded<string>();
            var progress = new ChannelProgress(queue[0].Writer);

            var buildTask = Task.Run(async () =>
            {
                try
                {
                    await client.Images.BuildImageFromDockerfileAsync(parameters, tarStream, null, null, progress, ct);
                }
                finally
                {
                    queue[0].Writer.TryComplete();
                }
            }, ct);

            await foreach (var line in queue[0].Reader.ReadAllAsync(ct))
            {
                yield return line;
            }
            await buildTask;
        }
        finally
        {
            if (File.Exists(tarPath))
            {
                File.Delete(tarPath);
            }
        }
    }

    public async Task<ImageInfo?> GetImageAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var inspect = await client.Images.InspectImageAsync(reference, ct);
            return new ImageInfo
            {
                Id = inspect.ID,
                Tags = inspect.RepoTags is null ? [] : [.. inspect.RepoTags],
                SizeBytes = inspect.Size,
                CreatedAt = inspect.Created
            };
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveImageAsync(string reference, string outputTarPath, CancellationToken ct = default)
    {
        await using var stream = await client.Images.SaveImageAsync(reference, ct);
        await using var output = File.Create(outputTarPath);
        await stream.CopyToAsync(output, ct);
    }

    public void Dispose()
    {
        client.Dispose();
    }

    private sealed class ChannelProgress : IProgress<JSONMessage>
    {
        private readonly System.Threading.Channels.ChannelWriter<string> writer;

        public ChannelProgress(System.Threading.Channels.ChannelWriter<string> writer)
        {
            this.writer = writer;
        }

        public void Report(JSONMessage value)
        {
            var line = !string.IsNullOrEmpty(value.Stream)
                ? value.Stream.TrimEnd('\r', '\n')
                : !string.IsNullOrEmpty(value.Status)
                    ? value.Status
                    : value.ErrorMessage ?? string.Empty;
            if (line.Length > 0)
            {
                writer.TryWrite(line);
            }
        }
    }
}
