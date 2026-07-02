using System.Text.Json;

namespace Kubernator.Web.Jobs;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record JobRecord
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required JobStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required IReadOnlyList<JobProgressEntry> Progress { get; init; }
    public string? Error { get; init; }
    public JsonElement? Result { get; init; }
    public string? KeyId { get; init; }
    public string? KeyName { get; init; }
    public TimeSpan? Duration =>
        StartedAt is { } s && CompletedAt is { } c ? c - s : null;
}

public sealed record JobProgressEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Message { get; init; }
}

public sealed class JobContext
{
    private readonly Action<string> reporter;

    internal JobContext(string id, Action<string> reporter)
    {
        Id = id;
        this.reporter = reporter;
    }

    public string Id { get; }

    public void Report(string message) => reporter(message);

    public IProgress<string> AsProgress() => new ProgressAdapter(reporter);

    private sealed class ProgressAdapter(Action<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink(value);
    }
}
