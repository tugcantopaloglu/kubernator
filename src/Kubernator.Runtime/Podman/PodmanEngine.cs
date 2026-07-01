using Kubernator.Core.Containers;
using Kubernator.Runtime.Docker;

namespace Kubernator.Runtime.Podman;

public sealed class PodmanEngine : IContainerEngine
{
    private readonly DockerEngine inner;

    public PodmanEngine(DockerEngine inner)
    {
        this.inner = inner;
    }

    public string Kind => "podman";

    public async Task<EngineInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var info = await inner.GetInfoAsync(ct);
        return info with { Name = "podman" };
    }

    public IAsyncEnumerable<string> BuildAsync(BuildContext context, CancellationToken ct = default) =>
        inner.BuildAsync(context, ct);

    public Task<ImageInfo?> GetImageAsync(string reference, CancellationToken ct = default) =>
        inner.GetImageAsync(reference, ct);

    public Task SaveImageAsync(string reference, string outputTarPath, CancellationToken ct = default) =>
        inner.SaveImageAsync(reference, outputTarPath, ct);

    public IAsyncEnumerable<string> PullImageAsync(string reference, string? platform = null, CancellationToken ct = default) =>
        inner.PullImageAsync(reference, platform, ct);

    public Task LoadImageAsync(string tarPath, CancellationToken ct = default) =>
        inner.LoadImageAsync(tarPath, ct);

    public Task TagImageAsync(string sourceReference, string targetReference, CancellationToken ct = default) =>
        inner.TagImageAsync(sourceReference, targetReference, ct);

    public IAsyncEnumerable<string> PushImageAsync(string reference, CancellationToken ct = default) =>
        inner.PushImageAsync(reference, ct);
}
