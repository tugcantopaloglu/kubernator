using Kubernator.Core.Abstractions;
using Kubernator.Core.Analysis;
using Kubernator.Core.Analysis.DotNet;
using Kubernator.Core.Detection;
using Kubernator.Core.Detection.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kubernator.Core.DependencyInjection;

public static class KubernatorCoreServiceCollectionExtensions
{
    public static IServiceCollection AddKubernatorCore(this IServiceCollection services)
    {
        services.AddTransient<IAppDetector, DotNetDetector>();
        services.AddTransient<IAppAnalyzer, DotNetAnalyzer>();

        services.TryAddSingleton<IDetectionService, DetectionService>();
        services.TryAddSingleton<IAnalysisService, AnalysisService>();

        return services;
    }
}
