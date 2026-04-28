using Docker.DotNet;
using Kubernator.Core.Containers;

namespace Kubernator.Runtime.Docker;

public sealed class DockerEngineProvider : IContainerEngineProvider
{
    public async Task<IContainerEngine> ResolveAsync(CancellationToken ct = default)
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

        return new DockerEngine(client);
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
