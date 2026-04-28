namespace Kubernator.Core.Models;

public sealed record NetworkInfo
{
    public IReadOnlyList<int> Ports { get; init; } = [];
    public IReadOnlyList<string> Urls { get; init; } = [];
    public bool ListensHttp { get; init; }
    public bool ListensHttps { get; init; }
    public bool RequiresIngress { get; init; }
}
