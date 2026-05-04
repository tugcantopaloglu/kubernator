namespace Kubernator.Core.Validation;

public sealed record ProcessOutcome
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }

    public bool Ok => ExitCode == 0;
}

public sealed record ProcessInvocation
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public interface IProcessRunner
{
    Task<ProcessOutcome> RunAsync(ProcessInvocation invocation, CancellationToken ct = default);
}
