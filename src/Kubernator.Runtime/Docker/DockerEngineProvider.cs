using Docker.DotNet;
using Kubernator.Core.Containers;

namespace Kubernator.Runtime.Docker;

public sealed class DockerEngineProvider : IContainerEngineProvider
{
    public Task<IContainerEngine> ResolveAsync(CancellationToken ct = default) =>
        ResolveAsync(requireMultiPlatform: false, ct);

    public async Task<IContainerEngine> ResolveAsync(bool requireMultiPlatform, CancellationToken ct = default)
    {
        var endpoint = ResolveEndpoint();
        var client = new DockerClientConfiguration(endpoint).CreateClient();

        try
        {
            await client.System.PingAsync(ct);
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"Could not reach a container engine at {endpoint}. Ensure Docker or a compatible daemon is running.", ex);
        }

        var docker = new DockerEngine(client);
        var buildxAvailable = await BuildxEngine.IsAvailableAsync(ct);

        if (requireMultiPlatform && !buildxAvailable)
        {
            client.Dispose();
            throw new InvalidOperationException(
                "Multi-platform build was requested but `docker buildx` is not available on PATH.");
        }

        return buildxAvailable ? new BuildxEngine(docker) : docker;
    }

    private static Uri ResolveEndpoint()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return new Uri(fromEnv);
        }
        return OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
    }
}
