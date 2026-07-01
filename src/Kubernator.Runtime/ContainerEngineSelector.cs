using Kubernator.Core.Containers;
using Kubernator.Runtime.Docker;
using Kubernator.Runtime.Podman;

namespace Kubernator.Runtime;

public sealed class ContainerEngineSelector : IContainerEngineProvider
{
    private const string EnvVarName = "KUBERNATOR_CONTAINER_ENGINE";

    private readonly IContainerEngineProvider docker;
    private readonly IContainerEngineProvider podman;

    public ContainerEngineSelector() : this(new DockerEngineProvider(), new PodmanEngineProvider())
    {
    }

    internal ContainerEngineSelector(IContainerEngineProvider dockerProvider, IContainerEngineProvider podmanProvider)
    {
        docker = dockerProvider;
        podman = podmanProvider;
    }

    public Task<IContainerEngine> ResolveAsync(CancellationToken ct = default) =>
        ResolveAsync(requireMultiPlatform: false, ct);

    public Task<IContainerEngine> ResolveAsync(bool requireMultiPlatform, CancellationToken ct = default)
    {
        var preference = Environment.GetEnvironmentVariable(EnvVarName)?.Trim().ToLowerInvariant();

        return preference switch
        {
            "docker" => docker.ResolveAsync(requireMultiPlatform, ct),
            "podman" => podman.ResolveAsync(requireMultiPlatform, ct),
            null or "" or "auto" => ResolveAutoAsync(requireMultiPlatform, ct),
            _ => throw new InvalidOperationException($"unknown {EnvVarName} '{preference}' — expected 'docker', 'podman', or 'auto'")
        };
    }

    private async Task<IContainerEngine> ResolveAutoAsync(bool requireMultiPlatform, CancellationToken ct)
    {
        try
        {
            return await docker.ResolveAsync(requireMultiPlatform, ct);
        }
        catch (InvalidOperationException dockerError)
        {
            try
            {
                return await podman.ResolveAsync(requireMultiPlatform, ct);
            }
            catch (InvalidOperationException podmanError)
            {
                throw new InvalidOperationException(
                    $"Could not reach Docker ({dockerError.Message}) or Podman ({podmanError.Message}). " +
                    $"Install/start one of them, or set {EnvVarName}=docker|podman explicitly.");
            }
        }
    }
}
