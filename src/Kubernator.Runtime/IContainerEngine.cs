namespace Kubernator.Runtime;

public sealed record BuildContext
{
    public required string ContextDirectory { get; init; }
    public required string DockerfilePath { get; init; }
    public required string ImageName { get; init; }
    public required string ImageTag { get; init; }
    public IReadOnlyDictionary<string, string> BuildArgs { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> AdditionalTags { get; init; } = [];
}

public sealed record ImageInfo
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record EngineInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ApiVersion { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
}

public interface IContainerEngine
{
    string Kind { get; }

    Task<EngineInfo> GetInfoAsync(CancellationToken ct = default);

    IAsyncEnumerable<string> BuildAsync(BuildContext context, CancellationToken ct = default);

    Task<ImageInfo?> GetImageAsync(string reference, CancellationToken ct = default);

    Task SaveImageAsync(string reference, string outputTarPath, CancellationToken ct = default);
}

public interface IContainerEngineProvider
{
    Task<IContainerEngine> ResolveAsync(CancellationToken ct = default);
}
