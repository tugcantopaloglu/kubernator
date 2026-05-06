using System.Runtime.CompilerServices;
using Kubernator.Core.Containers;

namespace Kubernator.Core.Tests.Packaging;

internal sealed class FakeContainerEngine : IContainerEngine
{
    private readonly Dictionary<string, ImageInfo> images = new(StringComparer.Ordinal);
    private bool simulateMissingThenBuild;

    public string Kind => "fake";
    public bool SupportsMultiPlatform { get; set; }

    public List<string> SavedImages { get; } = [];
    public List<(string Reference, string Platform)> SavedPerPlatform { get; } = [];
    public List<BuildContext> Builds { get; } = [];

    public void Register(string reference, long sizeBytes = 1024)
    {
        images[reference] = new ImageInfo
        {
            Id = $"sha256:{Guid.NewGuid():N}",
            Tags = [reference],
            SizeBytes = sizeBytes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void EnableSimulateMissingThenBuild()
    {
        simulateMissingThenBuild = true;
    }

    public Task<EngineInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new EngineInfo
    {
        Name = "fake",
        Version = "0.0.0",
        ApiVersion = "0.0",
        OperatingSystem = "linux",
        Architecture = "x86_64"
    });

    public async IAsyncEnumerable<string> BuildAsync(
        BuildContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Builds.Add(context);
        var reference = $"{context.ImageName}:{context.ImageTag}";
        Register(reference);
        yield return $"step 1/1: built {reference}";
        await Task.CompletedTask;
    }

    public Task<ImageInfo?> GetImageAsync(string reference, CancellationToken ct = default)
    {
        if (simulateMissingThenBuild && Builds.Count == 0)
        {
            return Task.FromResult<ImageInfo?>(null);
        }
        return Task.FromResult(images.TryGetValue(reference, out var info) ? info : null);
    }

    public async Task SaveImageAsync(string reference, string outputTarPath, CancellationToken ct = default)
    {
        SavedImages.Add(reference);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-image-tar:{reference}");
        await File.WriteAllBytesAsync(outputTarPath, bytes, ct);
    }

    public async Task SaveImageAsync(string reference, string platform, string outputTarPath, CancellationToken ct = default)
    {
        SavedImages.Add(reference);
        SavedPerPlatform.Add((reference, platform));
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-image-tar:{reference}:{platform}");
        await File.WriteAllBytesAsync(outputTarPath, bytes, ct);
    }

    public List<(string Reference, string? Platform)> Pulled { get; } = [];
    public List<string> Loaded { get; } = [];
    public List<(string Source, string Target)> Tagged { get; } = [];
    public List<string> Pushed { get; } = [];

    public async IAsyncEnumerable<string> PullImageAsync(string reference, string? platform = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Pulled.Add((reference, platform));
        Register(reference);
        yield return $"pulled {reference}";
        await Task.CompletedTask;
    }

    public Task LoadImageAsync(string tarPath, CancellationToken ct = default)
    {
        Loaded.Add(tarPath);
        return Task.CompletedTask;
    }

    public Task TagImageAsync(string sourceReference, string targetReference, CancellationToken ct = default)
    {
        Tagged.Add((sourceReference, targetReference));
        Register(targetReference);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> PushImageAsync(string reference, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Pushed.Add(reference);
        yield return $"pushed {reference}";
        await Task.CompletedTask;
    }
}
