using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kubernator.Core.Vault;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Kubernator.Core.ClusterProvisioning.Ssh;

public sealed class SshNodeExecutor : NodeExecutorBase
{
    private readonly IKeyVault vault;
    private readonly KnownHostsStore knownHosts;

    public SshNodeExecutor(IKeyVault vault, KnownHostsStore? knownHosts = null)
    {
        this.vault = vault;
        this.knownHosts = knownHosts ?? KnownHostsStore.Default();
    }

    public static async Task<string> ProbeHostKeyFingerprintAsync(NodeConnection connection, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            throw new InvalidOperationException("Host is required");
        }

        string? fingerprint = null;
        var probeUser = string.IsNullOrWhiteSpace(connection.Username) ? "probe" : connection.Username;
        var connectionInfo = new ConnectionInfo(
            connection.Host, connection.Port, probeUser,
            new NoneAuthenticationMethod(probeUser))
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, e) =>
        {
            fingerprint = Convert.ToBase64String(SHA256.HashData(e.HostKey));
            e.CanTrust = true;
        };

        try
        {
            await Task.Run(client.Connect, ct);
        }
        catch (Renci.SshNet.Common.SshAuthenticationException)
        {
        }
        catch (Renci.SshNet.Common.SshConnectionException)
        {
        }
        finally
        {
            if (client.IsConnected)
            {
                try { client.Disconnect(); } catch { }
            }
        }

        return fingerprint ?? throw new InvalidOperationException($"did not receive a host key from {connection.Host}:{connection.Port}");
    }

    public override async Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection,
        NodeCommand command,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var effective = command.UseSudo ? ShellCommand.WrapSudo(command.CommandLine, connection.SudoPassword) : command.CommandLine;

        using var client = await ConnectSshAsync(connection, ct);
        using var sshCommand = client.CreateCommand(effective);
        sshCommand.CommandTimeout = command.Timeout;

        var started = Stopwatch.GetTimestamp();
        var stdout = new StringBuilder();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(command.Timeout);

        var asyncResult = sshCommand.BeginExecute();
        using var reader = new StreamReader(sshCommand.OutputStream);
        try
        {
            while (!asyncResult.IsCompleted)
            {
                var line = await reader.ReadLineAsync(timeoutCts.Token);
                if (line is not null)
                {
                    stdout.AppendLine(line);
                    progress?.Report(line);
                }
                else
                {
                    await Task.Delay(50, timeoutCts.Token);
                }
            }

            string? tail;
            while ((tail = await reader.ReadLineAsync(CancellationToken.None)) is not null)
            {
                stdout.AppendLine(tail);
                progress?.Report(tail);
            }

            sshCommand.EndExecute(asyncResult);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new NodeExecOutcome
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = $"timed out after {command.Timeout}",
                Duration = Stopwatch.GetElapsedTime(started)
            };
        }

        return new NodeExecOutcome
        {
            ExitCode = sshCommand.ExitStatus ?? -1,
            StandardOutput = stdout.ToString(),
            StandardError = sshCommand.Error ?? string.Empty,
            Duration = Stopwatch.GetElapsedTime(started)
        };
    }

    public override async Task UploadFileAsync(
        NodeConnection connection,
        string localPath,
        string remotePath,
        UnixFileMode? mode = null,
        bool useSudo = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var stagingPath = useSudo ? $"/tmp/.kubernator-{Guid.NewGuid():N}" : remotePath;
        var dir = PosixPath.GetDirectory(remotePath);
        await ExecuteAsync(connection, new NodeCommand { CommandLine = $"mkdir -p {ShellCommand.Quote(dir)}", UseSudo = useSudo }, null, ct);

        progress?.Report($"uploading {PosixPath.GetFileName(localPath.Replace('\\', '/'))} -> {remotePath}");
        using (var sftp = await ConnectSftpAsync(connection, ct))
        {
            await using var stream = File.OpenRead(localPath);
            await Task.Run(() => sftp.UploadFile(stream, stagingPath, canOverride: true), ct);
            if (!useSudo && mode is { } directMode)
            {
                sftp.ChangePermissions(stagingPath, Convert.ToInt16(ShellCommand.ToOctal(directMode), 8));
            }
        }

        if (useSudo)
        {
            var modeArg = mode is { } m ? $" && chmod {ShellCommand.ToOctal(m)} {ShellCommand.Quote(remotePath)}" : string.Empty;
            var script = $"mv {ShellCommand.Quote(stagingPath)} {ShellCommand.Quote(remotePath)}{modeArg}";
            var outcome = await ExecuteAsync(connection, new NodeCommand { CommandLine = script, UseSudo = true }, progress, ct);
            if (!outcome.Ok)
            {
                throw new InvalidOperationException($"failed to place {remotePath}: {outcome.StandardError}");
            }
        }
    }

    private async Task<SshClient> ConnectSshAsync(NodeConnection connection, CancellationToken ct)
    {
        var connectionInfo = await BuildConnectionInfoAsync(connection, ct);
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, e) => VerifyHostKey(connection, e);
        await Task.Run(client.Connect, ct);
        return client;
    }

    private async Task<SftpClient> ConnectSftpAsync(NodeConnection connection, CancellationToken ct)
    {
        var connectionInfo = await BuildConnectionInfoAsync(connection, ct);
        var client = new SftpClient(connectionInfo);
        client.HostKeyReceived += (_, e) => VerifyHostKey(connection, e);
        await Task.Run(client.Connect, ct);
        return client;
    }

    private async Task<ConnectionInfo> BuildConnectionInfoAsync(NodeConnection connection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Username))
        {
            throw new InvalidOperationException("SSH connection requires Host and Username");
        }

        var authMethods = new List<AuthenticationMethod>();
        var keyPath = await ResolvePrivateKeyPathAsync(connection, ct);
        if (keyPath is not null)
        {
            var keyFile = string.IsNullOrEmpty(connection.SshPrivateKeyPassphrase)
                ? new PrivateKeyFile(keyPath)
                : new PrivateKeyFile(keyPath, connection.SshPrivateKeyPassphrase);
            authMethods.Add(new PrivateKeyAuthenticationMethod(connection.Username, keyFile));
        }
        if (!string.IsNullOrEmpty(connection.SshPassword))
        {
            authMethods.Add(new PasswordAuthenticationMethod(connection.Username, connection.SshPassword));
        }
        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException($"no SSH credentials configured for {connection.Host}");
        }

        return new ConnectionInfo(connection.Host, connection.Port, connection.Username, [.. authMethods])
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private async Task<string?> ResolvePrivateKeyPathAsync(NodeConnection connection, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(connection.SshPrivateKeyVaultId))
        {
            return await vault.ResolvePathAsync(connection.SshPrivateKeyVaultId, ct);
        }
        return connection.SshPrivateKeyPath;
    }

    private void VerifyHostKey(NodeConnection connection, HostKeyEventArgs e)
    {
        var fingerprint = Convert.ToBase64String(SHA256.HashData(e.HostKey));
        var host = connection.Host!;

        if (!string.IsNullOrEmpty(connection.ExpectedHostKeyFingerprint))
        {
            e.CanTrust = string.Equals(connection.ExpectedHostKeyFingerprint, fingerprint, StringComparison.Ordinal);
            return;
        }

        if (knownHosts.TryGet(host, connection.Port, out var trusted))
        {
            e.CanTrust = string.Equals(trusted, fingerprint, StringComparison.Ordinal);
            return;
        }

        if (connection.AllowInsecureHostKey)
        {
            knownHosts.Trust(host, connection.Port, fingerprint);
            e.CanTrust = true;
            return;
        }

        e.CanTrust = false;
    }
}
