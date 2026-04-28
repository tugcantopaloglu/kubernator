using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Kubernator.Cli.Infrastructure;

internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection services;

    public TypeRegistrar(IServiceCollection services)
    {
        this.services = services;
    }

    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(service, _ => factory());
    }
}

internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider provider;

    public TypeResolver(ServiceProvider provider)
    {
        this.provider = provider;
    }

    public object? Resolve(Type? type)
    {
        return type is null ? null : provider.GetService(type);
    }

    public void Dispose()
    {
        provider.Dispose();
    }
}
