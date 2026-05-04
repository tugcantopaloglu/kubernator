namespace Kubernator.Core.Updates;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(string sourceUrl, CancellationToken ct = default);

    Task<UpdateApplyResult> ApplyAsync(
        string sourceUrl,
        string? runtimeIdentifierOverride,
        string? targetExecutablePath,
        IProgress<string>? progress,
        CancellationToken ct = default);
}
