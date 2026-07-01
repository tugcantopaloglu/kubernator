namespace Kubernator.Core.ClusterProvisioning.Ssh;

public sealed class NodeExecutor : INodeExecutor
{
    private readonly LocalNodeExecutor local = new();
    private readonly SshNodeExecutor ssh;

    public NodeExecutor(SshNodeExecutor ssh)
    {
        this.ssh = ssh;
    }

    public Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection,
        NodeCommand command,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        Resolve(connection).ExecuteAsync(connection, command, progress, ct);

    public Task UploadFileAsync(
        NodeConnection connection,
        string localPath,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        Resolve(connection).UploadFileAsync(connection, localPath, remotePath, mode, useSudo, progress, ct);

    public Task UploadTextAsync(
        NodeConnection connection,
        string content,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        Resolve(connection).UploadTextAsync(connection, content, remotePath, mode, useSudo, progress, ct);

    public Task<bool> TestConnectionAsync(NodeConnection connection, CancellationToken ct = default) =>
        Resolve(connection).TestConnectionAsync(connection, ct);

    private INodeExecutor Resolve(NodeConnection connection) =>
        connection.Mode == NodeConnectionMode.Ssh ? ssh : local;
}
