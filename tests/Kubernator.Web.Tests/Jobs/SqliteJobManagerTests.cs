using Kubernator.Web.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Web.Tests.Jobs;

public sealed class SqliteJobManagerTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), $"kubn-jobs-test-{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(home, "jobs.db");

    public void Dispose()
    {
        try { Directory.Delete(home, recursive: true); } catch { }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition was not met in time");
            }
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Enqueue_runs_the_matching_handler_and_persists_the_result()
    {
        using var manager = new SqliteJobManager(DbPath);
        var handler = new DelegateJobHandler<string>("echo", (payload, ctx, ct) =>
        {
            ctx.Report($"handling {payload}");
            return Task.FromResult<object?>(new EchoResult(payload));
        });
        using var runner = new JobBackgroundRunner(manager, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: 1);
        await runner.StartAsync(CancellationToken.None);
        try
        {
            var record = manager.Enqueue("echo", "hello");

            await WaitUntilAsync(() => manager.Get(record.Id)!.Status is JobStatus.Succeeded or JobStatus.Failed, TimeSpan.FromSeconds(5));

            var final = manager.Get(record.Id)!;
            final.Status.Should().Be(JobStatus.Succeeded);
            final.Result!.Value.GetProperty("message").GetString().Should().Be("hello");
            final.Progress.Should().ContainSingle(p => p.Message == "handling hello");
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cancel_of_a_queued_job_prevents_the_handler_from_running()
    {
        using var manager = new SqliteJobManager(DbPath);
        var invoked = 0;
        var handler = new DelegateJobHandler<string>("noop", (payload, ctx, ct) =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult<object?>(null);
        });

        // No runner started: the job sits in the queue so Cancel must hit the "still queued" path.
        var record = manager.Enqueue("noop", "x");
        manager.Cancel(record.Id).Should().BeTrue();

        manager.Get(record.Id)!.Status.Should().Be(JobStatus.Cancelled);

        using var runner = new JobBackgroundRunner(manager, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: 1);
        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await runner.StopAsync(CancellationToken.None);

        invoked.Should().Be(0);
        manager.Get(record.Id)!.Status.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_of_a_running_job_stops_it_via_its_cancellation_token()
    {
        using var manager = new SqliteJobManager(DbPath);
        var started = new TaskCompletionSource();
        var handler = new DelegateJobHandler<string>("slow", async (payload, ctx, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        using var runner = new JobBackgroundRunner(manager, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: 1);
        await runner.StartAsync(CancellationToken.None);
        try
        {
            var record = manager.Enqueue("slow", "x");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            manager.Cancel(record.Id).Should().BeTrue();

            await WaitUntilAsync(() => manager.Get(record.Id)!.Status == JobStatus.Cancelled, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Multiple_workers_execute_jobs_concurrently_instead_of_one_at_a_time()
    {
        using var manager = new SqliteJobManager(DbPath);
        const int concurrency = 3;
        using var allEntered = new Barrier(concurrency);
        var handler = new DelegateJobHandler<string>("parallel", (payload, ctx, ct) =>
        {
            // Every worker blocks here until `concurrency` of them have arrived simultaneously.
            // With one-job-at-a-time processing this would hang until the test times out.
            allEntered.SignalAndWait(TimeSpan.FromSeconds(5), ct);
            return Task.FromResult<object?>(null);
        });
        using var runner = new JobBackgroundRunner(manager, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: concurrency);
        await runner.StartAsync(CancellationToken.None);
        try
        {
            var ids = Enumerable.Range(0, concurrency)
                .Select(i => manager.Enqueue("parallel", $"job-{i}").Id)
                .ToArray();

            await WaitUntilAsync(() => ids.All(id => manager.Get(id)!.Status == JobStatus.Succeeded), TimeSpan.FromSeconds(10));
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_still_marked_Running_after_a_restart_is_requeued_and_completes()
    {
        string jobId;
        using (var crashed = new SqliteJobManager(DbPath))
        {
            var record = crashed.Enqueue("resume-me", "payload");
            jobId = record.Id;
            // Simulate the previous process dying mid-execution: flip to Running but never call Complete.
            crashed.BeginExecution(jobId).Should().NotBeNull();
        }

        using var recovered = new SqliteJobManager(DbPath);
        recovered.Get(jobId)!.Status.Should().Be(JobStatus.Queued, "orphaned Running jobs must be requeued on startup");

        var handler = new DelegateJobHandler<string>("resume-me", (payload, ctx, ct) => Task.FromResult<object?>(new EchoResult(payload)));
        using var runner = new JobBackgroundRunner(recovered, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: 1);
        await runner.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => recovered.Get(jobId)!.Status == JobStatus.Succeeded, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Handler_exception_marks_the_job_Failed_with_the_error_message()
    {
        using var manager = new SqliteJobManager(DbPath);
        var handler = new DelegateJobHandler<string>("boom", (payload, ctx, ct) =>
            throw new InvalidOperationException("kaboom"));
        using var runner = new JobBackgroundRunner(manager, [handler], NullLogger<JobBackgroundRunner>.Instance, workerCount: 1);
        await runner.StartAsync(CancellationToken.None);
        try
        {
            var record = manager.Enqueue("boom", "x");
            await WaitUntilAsync(() => manager.Get(record.Id)!.Status == JobStatus.Failed, TimeSpan.FromSeconds(5));

            manager.Get(record.Id)!.Error.Should().Contain("kaboom");
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void List_orders_jobs_newest_first_and_respects_the_limit()
    {
        using var manager = new SqliteJobManager(DbPath);
        for (var i = 0; i < 5; i++)
        {
            manager.Enqueue("noop", $"job-{i}");
        }

        var page = manager.List(2);

        page.Should().HaveCount(2);
        page[0].CreatedAt.Should().BeOnOrAfter(page[1].CreatedAt);
    }

    private sealed record EchoResult(string Message);

    private sealed class DelegateJobHandler<TPayload>(string kind, Func<TPayload, JobContext, CancellationToken, Task<object?>> run)
        : JobHandler<TPayload>
    {
        public override string Kind => kind;

        protected override Task<object?> RunAsync(TPayload payload, JobContext ctx, CancellationToken ct)
            => run(payload, ctx, ct);
    }
}
