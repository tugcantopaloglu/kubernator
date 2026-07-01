using Kubernator.Core.Containers;
using Kubernator.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kubernator.Runtime.DependencyInjection;

public static class KubernatorRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddKubernatorRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IContainerEngineProvider, ContainerEngineSelector>();
        return services;
    }
}
