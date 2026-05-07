using Kubernator.Web.Jobs;

namespace Kubernator.Web.Api;

public sealed record JobDto
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public required IReadOnlyList<JobProgressDto> Progress { get; init; }
    public string? Error { get; init; }
    public object? Result { get; init; }
    public string? KeyId { get; init; }
    public string? KeyName { get; init; }

    public static JobDto From(JobRecord r) => new()
    {
        Id = r.Id,
        Kind = r.Kind,
        Status = r.Status.ToString(),
        CreatedAt = r.CreatedAt,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        DurationMs = r.Duration is { } d ? (long)d.TotalMilliseconds : null,
        Progress = r.Progress.Select(p => new JobProgressDto { Timestamp = p.Timestamp, Message = p.Message }).ToArray(),
        Error = r.Error,
        Result = r.Result,
        KeyId = r.KeyId,
        KeyName = r.KeyName
    };
}

public sealed record JobProgressDto
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Message { get; init; }
}

public sealed record JobListResponse
{
    public required IReadOnlyList<JobDto> Jobs { get; init; }
}

public sealed record JobAcceptedResponse
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Status { get; init; }
    public required string Location { get; init; }
}
