using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class RecordingNodeExecutor : INodeExecutor
{
    public sealed record ExecCall(NodeConnection Connection, NodeCommand Command);
    public sealed record UploadCall(NodeConnection Connection, string RemotePath, bool UseSudo, string? Content = null);

    public List<ExecCall> ExecCalls { get; } = [];
    public List<UploadCall> UploadCalls { get; } = [];
    public NodeExecOutcome Default { get; set; } = new() { ExitCode = 0, StandardOutput = "", StandardError = "", Duration = TimeSpan.Zero };
    public Func<ExecCall, NodeExecOutcome>? Responder { get; set; }
    public bool TestConnectionResult { get; set; } = true;

    public Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection, NodeCommand command, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var call = new ExecCall(connection, command);
        lock (ExecCalls)
        {
            ExecCalls.Add(call);
        }
        return Task.FromResult(Responder is not null ? Responder(call) : Default);
    }

    public Task UploadFileAsync(
        NodeConnection connection, string localPath, string remotePath, UnixFileMode? mode = null,
        bool useSudo = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (UploadCalls)
        {
            UploadCalls.Add(new UploadCall(connection, remotePath, useSudo, localPath));
        }
        return Task.CompletedTask;
    }

    public Task UploadTextAsync(
        NodeConnection connection, string content, string remotePath, UnixFileMode? mode = null,
        bool useSudo = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (UploadCalls)
        {
            UploadCalls.Add(new UploadCall(connection, remotePath, useSudo, content));
        }
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(NodeConnection connection, CancellationToken ct = default) =>
        Task.FromResult(TestConnectionResult);
}
