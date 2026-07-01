namespace Kubernator.Core.ClusterProvisioning.Ssh;

public abstract class NodeExecutorBase : INodeExecutor
{
    public abstract Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection,
        NodeCommand command,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    public abstract Task UploadFileAsync(
        NodeConnection connection,
        string localPath,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    public virtual async Task UploadTextAsync(
        NodeConnection connection,
        string content,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kubernator-text-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tempFile, content, ct);
        try
        {
            await UploadFileAsync(connection, tempFile, remotePath, mode, useSudo, progress, ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    public virtual async Task<bool> TestConnectionAsync(NodeConnection connection, CancellationToken ct = default)
    {
        try
        {
            var outcome = await ExecuteAsync(
                connection,
                new NodeCommand { CommandLine = "echo ok", Timeout = TimeSpan.FromSeconds(15) },
                null,
                ct);
            return outcome.Ok;
        }
        catch
        {
            return false;
        }
    }
}
