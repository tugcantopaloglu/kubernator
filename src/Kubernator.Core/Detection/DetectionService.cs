using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Detection;

public sealed class DetectionService : IDetectionService
{
    private readonly IReadOnlyList<IAppDetector> detectors;
    private readonly ILogger<DetectionService> logger;

    public DetectionService(IEnumerable<IAppDetector> detectors, ILogger<DetectionService> logger)
    {
        this.detectors = detectors.ToArray();
        this.logger = logger;
    }

    public async Task<IReadOnlyList<DetectionResult>> DetectAllAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new DirectoryNotFoundException($"Path not found: {path}");
        }

        var resolved = Path.GetFullPath(path);
        var tasks = detectors.Select(d => SafeDetectAsync(d, resolved, ct));
        var results = await Task.WhenAll(tasks);

        return [.. results
            .Where(r => r.Confidence > 0)
            .OrderByDescending(r => r.Confidence)];
    }

    public async Task<DetectionResult> DetectBestAsync(string path, CancellationToken ct = default)
    {
        var all = await DetectAllAsync(path, ct);
        return all.Count > 0 ? all[0] : DetectionResult.None(Path.GetFullPath(path));
    }

    private async Task<DetectionResult> SafeDetectAsync(IAppDetector detector, string path, CancellationToken ct)
    {
        try
        {
            return await detector.DetectAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Detector {Detector} failed for {Path}", detector.GetType().Name, path);
            return DetectionResult.None(path);
        }
    }
}
