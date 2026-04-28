using Kubernator.Core.Models;

namespace Kubernator.Core.Abstractions;

public interface IAppDetector
{
    AppKind Handles { get; }

    Task<DetectionResult> DetectAsync(string path, CancellationToken ct = default);
}
