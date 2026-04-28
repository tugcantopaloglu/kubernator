using Kubernator.Core.Models;

namespace Kubernator.Core.Abstractions;

public interface IDetectionService
{
    Task<IReadOnlyList<DetectionResult>> DetectAllAsync(string path, CancellationToken ct = default);

    Task<DetectionResult> DetectBestAsync(string path, CancellationToken ct = default);
}
