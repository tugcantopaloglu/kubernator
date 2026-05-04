using System.Diagnostics;
using System.Text;

namespace Kubernator.Core.Validation;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = invocation.WorkingDirectory ?? Environment.CurrentDirectory
        };
        foreach (var arg in invocation.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        if (invocation.Environment is { Count: > 0 })
        {
            foreach (var (k, v) in invocation.Environment)
            {
                psi.Environment[k] = v;
            }
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        var started = Stopwatch.GetTimestamp();
        if (!process.Start())
        {
            throw new InvalidOperationException($"failed to start process {invocation.FileName}");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(invocation.Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessOutcome
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.AppendLine($"timed out after {invocation.Timeout}").ToString(),
                Duration = Stopwatch.GetElapsedTime(started)
            };
        }

        process.WaitForExit();

        return new ProcessOutcome
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            Duration = Stopwatch.GetElapsedTime(started)
        };
    }
}
