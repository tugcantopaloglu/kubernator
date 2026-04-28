using Kubernator.Core.Abstractions;
using Kubernator.Core.Analysis;
using Kubernator.Core.Analysis.DotNet;
using Kubernator.Core.Analysis.Go;
using Kubernator.Core.Analysis.Java;
using Kubernator.Core.Analysis.Node;
using Kubernator.Core.Analysis.Python;
using Kubernator.Core.Analysis.Static;
using Kubernator.Core.Detection;
using Kubernator.Core.Detection.DotNet;
using Kubernator.Core.Detection.Go;
using Kubernator.Core.Detection.Java;
using Kubernator.Core.Detection.Node;
using Kubernator.Core.Detection.Python;
using Kubernator.Core.Detection.Static;
using Kubernator.Core.Generation;
using Kubernator.Core.Packaging;
using Kubernator.Core.Packaging.Signing;
using Kubernator.Core.Pipelines;
using Kubernator.Core.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kubernator.Core.DependencyInjection;

public static class KubernatorCoreServiceCollectionExtensions
{
    public static IServiceCollection AddKubernatorCore(this IServiceCollection services)
    {
        services.AddTransient<IAppDetector, DotNetDetector>();
        services.AddTransient<IAppDetector, NodeDetector>();
        services.AddTransient<IAppDetector, JavaDetector>();
        services.AddTransient<IAppDetector, PythonDetector>();
        services.AddTransient<IAppDetector, GoDetector>();
        services.AddTransient<IAppDetector, StaticWebDetector>();

        services.AddTransient<IAppAnalyzer, DotNetAnalyzer>();
        services.AddTransient<IAppAnalyzer, NodeAnalyzer>();
        services.AddTransient<IAppAnalyzer, JavaAnalyzer>();
        services.AddTransient<IAppAnalyzer, PythonAnalyzer>();
        services.AddTransient<IAppAnalyzer, GoAnalyzer>();
        services.AddTransient<IAppAnalyzer, StaticWebAnalyzer>();

        services.TryAddSingleton<IDetectionService, DetectionService>();
        services.TryAddSingleton<IAnalysisService, AnalysisService>();
        services.TryAddSingleton<IStrategySelector, StrategySelector>();
        services.TryAddSingleton<IGenerationService, GenerationService>();
        services.TryAddSingleton<IBundleService, BundleService>();
        services.TryAddSingleton<ICosignSigner, CosignSigner>();
        services.TryAddSingleton<IPipelineService, PipelineService>();

        return services;
    }
}
