using Microsoft.Extensions.Hosting;

namespace Kubernator.Web.Jobs;

public sealed class JobBackgroundRunner : BackgroundService
{
    private const int DefaultWorkerCount = 4;

    private readonly SqliteJobManager manager;
    private readonly IReadOnlyDictionary<string, IJobHandler> handlers;
    private readonly ILogger<JobBackgroundRunner> logger;
    private readonly int workerCount;

    public JobBackgroundRunner(IJobManager manager, IEnumerable<IJobHandler> handlers, ILogger<JobBackgroundRunner> logger)
        : this(manager, handlers, logger, ResolveWorkerCount())
    {
    }

    internal JobBackgroundRunner(IJobManager manager, IEnumerable<IJobHandler> handlers, ILogger<JobBackgroundRunner> logger, int workerCount)
    {
        this.manager = (SqliteJobManager)manager;
        this.handlers = handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);
        this.logger = logger;
        this.workerCount = workerCount;
    }

    private static int ResolveWorkerCount()
    {
        var raw = Environment.GetEnvironmentVariable("KUBERNATOR_JOB_WORKERS");
        return int.TryParse(raw, out var n) && n > 0 ? n : DefaultWorkerCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerLoopAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in manager.Reader.ReadAllAsync(stoppingToken))
        {
            await RunOneAsync(id, stoppingToken);
        }
    }

    private async Task RunOneAsync(string id, CancellationToken stoppingToken)
    {
        var execution = manager.BeginExecution(id);
        if (execution is null)
        {
            // Job was cancelled while still queued, or vanished — nothing to run.
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, execution.Cts.Token);
        var startedAt = DateTimeOffset.UtcNow;
        var jobCtx = new JobContext(id, msg =>
        {
            manager.AddProgress(id, msg);
            logger.LogInformation("[job {Id} {Kind}] {Message}", id, execution.Kind, msg);
        });

        try
        {
            if (!handlers.TryGetValue(execution.Kind, out var handler))
            {
                throw new InvalidOperationException($"no handler registered for job kind '{execution.Kind}'");
            }

            var result = await handler.ExecuteAsync(execution.PayloadJson, jobCtx, linked.Token);
            manager.Complete(id, JobStatus.Succeeded, result, null);
            logger.LogInformation("job {Id} {Kind} succeeded in {Ms}ms",
                id, execution.Kind, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            manager.Complete(id, JobStatus.Cancelled, null, "cancelled");
            logger.LogInformation("job {Id} {Kind} cancelled", id, execution.Kind);
        }
        catch (Exception ex)
        {
            manager.Complete(id, JobStatus.Failed, null, ex.GetType().Name + ": " + ex.Message);
            logger.LogError(ex, "job {Id} {Kind} failed", id, execution.Kind);
        }
        finally
        {
            manager.EndExecution(id);
        }
    }
}
