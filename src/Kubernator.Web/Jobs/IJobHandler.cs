using System.Text.Json;

namespace Kubernator.Web.Jobs;

public interface IJobHandler
{
    string Kind { get; }
    Task<object?> ExecuteAsync(string payloadJson, JobContext ctx, CancellationToken ct);
}

public abstract class JobHandler<TPayload> : IJobHandler
{
    public abstract string Kind { get; }

    public Task<object?> ExecuteAsync(string payloadJson, JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TPayload>(payloadJson, JobJson.Options)
            ?? throw new InvalidOperationException($"job '{Kind}' payload deserialized to null");
        return RunAsync(payload, ctx, ct);
    }

    protected abstract Task<object?> RunAsync(TPayload payload, JobContext ctx, CancellationToken ct);
}
