using Bunit;
using Kubernator.Web.Components.Shared;
using Kubernator.Web.Jobs;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Kubernator.Web.Tests.Components;

public sealed class JobProgressPanelTests : BunitContext
{
    [Fact]
    public async Task Reports_failure_instead_of_crashing_when_the_job_store_throws()
    {
        var jobs = Substitute.For<IJobManager>();
        jobs.Get(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("db is locked"));
        Services.AddSingleton(jobs);

        JobRecord? completed = null;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.OnCompleted, r => { completed = r; }));

        await cut.Instance.StartAsync("job-1");

        completed.Should().NotBeNull();
        completed!.Status.Should().Be(JobStatus.Failed);
        completed.Error.Should().Contain("db is locked");
        cut.Markup.Should().Contain("db is locked");
    }

    [Fact]
    public async Task Surfaces_terminal_job_state_to_the_completion_callback()
    {
        var record = new JobRecord
        {
            Id = "job-2",
            Kind = "cluster-pull",
            Status = JobStatus.Succeeded,
            CreatedAt = DateTimeOffset.UnixEpoch,
            Progress = new[] { new JobProgressEntry { Timestamp = DateTimeOffset.UnixEpoch, Message = "done" } }
        };
        var jobs = Substitute.For<IJobManager>();
        jobs.Get("job-2").Returns(record);
        Services.AddSingleton(jobs);

        JobRecord? completed = null;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.OnCompleted, r => { completed = r; }));

        await cut.Instance.StartAsync("job-2");

        completed.Should().NotBeNull();
        completed!.Status.Should().Be(JobStatus.Succeeded);
        cut.Markup.Should().Contain("done");
    }
}
