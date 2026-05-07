using Microsoft.Extensions.Hosting;

namespace Kubernator.Web.Jobs;

public sealed class JobBackgroundRunner : BackgroundService
{
    private readonly InMemoryJobManager manager;
    private readonly ILogger<JobBackgroundRunner> logger;

    public JobBackgroundRunner(IJobManager manager, ILogger<JobBackgroundRunner> logger)
    {
        this.manager = (InMemoryJobManager)manager;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var state in manager.Reader.ReadAllAsync(stoppingToken))
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, state.Cts.Token);
            await RunOneAsync(state, linked.Token);
        }
    }

    private async Task RunOneAsync(JobState state, CancellationToken ct)
    {
        lock (state.Sync)
        {
            state.Status = JobStatus.Running;
            state.StartedAt = DateTimeOffset.UtcNow;
        }
        var jobCtx = new JobContext(state.Id, msg =>
        {
            state.AddProgress(msg);
            logger.LogInformation("[job {Id} {Kind}] {Message}", state.Id, state.Kind, msg);
        });

        try
        {
            var result = await state.Work(jobCtx, ct);
            lock (state.Sync)
            {
                state.Status = JobStatus.Succeeded;
                state.Result = result;
                state.CompletedAt = DateTimeOffset.UtcNow;
            }
            logger.LogInformation("job {Id} {Kind} succeeded in {Ms}ms",
                state.Id, state.Kind, (state.CompletedAt!.Value - state.StartedAt!.Value).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (state.Sync)
            {
                state.Status = JobStatus.Cancelled;
                state.CompletedAt = DateTimeOffset.UtcNow;
                state.Error = "cancelled";
            }
            logger.LogInformation("job {Id} {Kind} cancelled", state.Id, state.Kind);
        }
        catch (Exception ex)
        {
            lock (state.Sync)
            {
                state.Status = JobStatus.Failed;
                state.CompletedAt = DateTimeOffset.UtcNow;
                state.Error = ex.GetType().Name + ": " + ex.Message;
            }
            logger.LogError(ex, "job {Id} {Kind} failed", state.Id, state.Kind);
        }
    }
}
