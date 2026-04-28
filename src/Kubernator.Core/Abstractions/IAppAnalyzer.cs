using Kubernator.Core.Models;

namespace Kubernator.Core.Abstractions;

public interface IAppAnalyzer
{
    AppKind Handles { get; }

    Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default);
}
