namespace Kubernator.Core.ClusterProvisioning.Ssh;

public enum NodeConnectionMode
{
    Local,
    Ssh
}

public sealed record NodeConnection
{
    public required NodeConnectionMode Mode { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 22;
    public string? Username { get; init; }
    public string? SshPrivateKeyVaultId { get; init; }
    public string? SshPrivateKeyPath { get; init; }
    public string? SshPrivateKeyPassphrase { get; init; }
    public string? SshPassword { get; init; }
    public string? SudoPassword { get; init; }
    public string? ExpectedHostKeyFingerprint { get; init; }
    public bool AllowInsecureHostKey { get; init; }

    public static NodeConnection Local() => new() { Mode = NodeConnectionMode.Local };
}

public sealed record NodeCommand
{
    public required string CommandLine { get; init; }
    public bool UseSudo { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record NodeExecOutcome
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }

    public bool Ok => ExitCode == 0;
}

public interface INodeExecutor
{
    Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection,
        NodeCommand command,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task UploadFileAsync(
        NodeConnection connection,
        string localPath,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task UploadTextAsync(
        NodeConnection connection,
        string content,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<bool> TestConnectionAsync(NodeConnection connection, CancellationToken ct = default);
}
