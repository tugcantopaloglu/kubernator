using Kubernator.Runtime.Docker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kubernator.Runtime.DependencyInjection;

public static class KubernatorRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddKubernatorRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IContainerEngineProvider, DockerEngineProvider>();
        return services;
    }
}
