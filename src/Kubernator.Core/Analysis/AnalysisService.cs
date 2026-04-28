using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Analysis;

public sealed class AnalysisService : IAnalysisService
{
    private readonly IDetectionService detection;
    private readonly Dictionary<AppKind, IAppAnalyzer> analyzers;
    private readonly ILogger<AnalysisService> logger;

    public AnalysisService(
        IDetectionService detection,
        IEnumerable<IAppAnalyzer> analyzers,
        ILogger<AnalysisService> logger)
    {
        this.detection = detection;
        this.analyzers = analyzers.ToDictionary(a => a.Handles);
        this.logger = logger;
    }

    public async Task<AppDescriptor> AnalyzeAsync(string path, CancellationToken ct = default)
    {
        var best = await detection.DetectBestAsync(path, ct);

        if (best.Kind == AppKind.Unknown)
        {
            throw new InvalidOperationException(
                $"No supported application detected at '{path}'. Provide a published output directory or a supported source tree.");
        }

        if (!analyzers.TryGetValue(best.Kind, out var analyzer))
        {
            throw new InvalidOperationException($"No analyzer registered for {best.Kind}");
        }

        logger.LogInformation("Analyzing {Kind} application at {Path}", best.Kind, best.SourcePath);
        return await analyzer.AnalyzeAsync(best, ct);
    }
}
