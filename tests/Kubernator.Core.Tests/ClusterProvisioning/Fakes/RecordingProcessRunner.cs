using Kubernator.Core.Validation;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class RecordingProcessRunner : IProcessRunner
{
    public List<ProcessInvocation> Invocations { get; } = [];
    public ProcessOutcome Default { get; set; } = new() { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };

    public Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default)
    {
        lock (Invocations)
        {
            Invocations.Add(invocation);
        }
        return Task.FromResult(Default);
    }
}
