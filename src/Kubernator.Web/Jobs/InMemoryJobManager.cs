using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kubernator.Web.Jobs;

public sealed class InMemoryJobManager : IJobManager
{
    public const int MaxRetainedJobs = 200;
    public const int ProgressBuffer = 200;

    private readonly ConcurrentDictionary<string, JobState> jobs = new(StringComparer.Ordinal);
    private readonly Channel<JobState> queue = Channel.CreateUnbounded<JobState>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<JobState> Reader => queue.Reader;

    public JobRecord Enqueue(JobSubmission submission)
    {
        var id = Guid.NewGuid().ToString("N")[..16];
        var state = new JobState
        {
            Id = id,
            Kind = submission.Kind,
            Work = submission.Work,
            Status = JobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            KeyId = submission.KeyId,
            KeyName = submission.KeyName,
            Cts = new CancellationTokenSource()
        };
        jobs[id] = state;
        queue.Writer.TryWrite(state);
        EvictOldJobs();
        return state.Snapshot();
    }

    public JobRecord? Get(string id) => jobs.TryGetValue(id, out var state) ? state.Snapshot() : null;

    public IReadOnlyList<JobRecord> List(int limit = 100)
    {
        return jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .Select(j => j.Snapshot())
            .ToArray();
    }

    public bool Cancel(string id)
    {
        if (!jobs.TryGetValue(id, out var state)) return false;
        lock (state.Sync)
        {
            if (state.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled)
            {
                return false;
            }
            try { state.Cts.Cancel(); }
            catch { }
            return true;
        }
    }

    private void EvictOldJobs()
    {
        if (jobs.Count <= MaxRetainedJobs) return;
        var stale = jobs.Values
            .Where(j => j.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled)
            .OrderBy(j => j.CompletedAt ?? j.CreatedAt)
            .Take(jobs.Count - MaxRetainedJobs)
            .Select(j => j.Id)
            .ToArray();
        foreach (var id in stale)
        {
            if (jobs.TryRemove(id, out var s))
            {
                s.Cts.Dispose();
            }
        }
    }
}

public sealed class JobState
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required Func<JobContext, CancellationToken, Task<object?>> Work { get; init; }
    public required JobStatus Status { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public object? Result { get; set; }
    public string? KeyId { get; init; }
    public string? KeyName { get; init; }
    public required CancellationTokenSource Cts { get; init; }

    public object Sync { get; } = new();
    private readonly List<JobProgressEntry> progress = new();

    public void AddProgress(string message)
    {
        lock (Sync)
        {
            if (progress.Count >= InMemoryJobManager.ProgressBuffer)
            {
                progress.RemoveAt(0);
            }
            progress.Add(new JobProgressEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Message = message
            });
        }
    }

    public JobRecord Snapshot()
    {
        lock (Sync)
        {
            return new JobRecord
            {
                Id = Id,
                Kind = Kind,
                Status = Status,
                CreatedAt = CreatedAt,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                Progress = progress.ToArray(),
                Error = Error,
                Result = Result,
                KeyId = KeyId,
                KeyName = KeyName
            };
        }
    }
}
