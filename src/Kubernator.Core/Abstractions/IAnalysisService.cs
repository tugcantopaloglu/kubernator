using Kubernator.Core.Models;

namespace Kubernator.Core.Abstractions;

public interface IAnalysisService
{
    Task<AppDescriptor> AnalyzeAsync(string path, CancellationToken ct = default);
}
