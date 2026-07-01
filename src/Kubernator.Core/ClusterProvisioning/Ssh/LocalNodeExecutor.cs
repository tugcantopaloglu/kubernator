using System.Diagnostics;
using System.Text;

namespace Kubernator.Core.ClusterProvisioning.Ssh;

internal sealed class LocalNodeExecutor : NodeExecutorBase
{
    public override async Task<NodeExecOutcome> ExecuteAsync(
        NodeConnection connection,
        NodeCommand command,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var effective = command.UseSudo ? ShellCommand.WrapSudo(command.CommandLine, connection.SudoPassword) : command.CommandLine;

        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add(effective);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); progress?.Report(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); progress?.Report(e.Data); } };

        var started = Stopwatch.GetTimestamp();
        if (!process.Start())
        {
            throw new InvalidOperationException("failed to start local process");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(command.Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new NodeExecOutcome
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.AppendLine($"timed out after {command.Timeout}").ToString(),
                Duration = Stopwatch.GetElapsedTime(started)
            };
        }

        return new NodeExecOutcome
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
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
        if (!useSudo)
        {
            var dir = PosixPath.GetDirectory(remotePath);
            await ExecuteAsync(connection, new NodeCommand { CommandLine = $"mkdir -p {ShellCommand.Quote(dir)}" }, null, ct);
            progress?.Report($"copying {Path.GetFileName(localPath)} -> {remotePath}");
            File.Copy(localPath, remotePath, overwrite: true);
            if (mode is { } m && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(remotePath, m);
            }
            return;
        }

        var staged = Path.Combine(Path.GetTempPath(), $"kubernator-{Guid.NewGuid():N}");
        File.Copy(localPath, staged, overwrite: true);
        try
        {
            progress?.Report($"placing {Path.GetFileName(localPath)} -> {remotePath} (sudo)");
            var dir = PosixPath.GetDirectory(remotePath);
            var modeArg = mode is { } m ? $" && chmod {ShellCommand.ToOctal(m)} {ShellCommand.Quote(remotePath)}" : string.Empty;
            var script = $"mkdir -p {ShellCommand.Quote(dir)} && mv {ShellCommand.Quote(staged)} {ShellCommand.Quote(remotePath)}{modeArg}";
            var outcome = await ExecuteAsync(connection, new NodeCommand { CommandLine = script, UseSudo = true }, progress, ct);
            if (!outcome.Ok)
            {
                throw new InvalidOperationException($"failed to place {remotePath}: {outcome.StandardError}");
            }
        }
        finally
        {
            try { File.Delete(staged); } catch { }
        }
    }
}
