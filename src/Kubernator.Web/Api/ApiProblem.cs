using System.Text.Json.Serialization;

namespace Kubernator.Web.Api;

public sealed record ApiProblem
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    [JsonPropertyName("errors")]
    public IDictionary<string, string[]>? Errors { get; init; }
}
