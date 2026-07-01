using Docker.DotNet;
using Kubernator.Core.Containers;
using Kubernator.Runtime.Docker;

namespace Kubernator.Runtime.Podman;

public sealed class PodmanEngineProvider : IContainerEngineProvider
{
    public Task<IContainerEngine> ResolveAsync(CancellationToken ct = default) =>
        ResolveAsync(requireMultiPlatform: false, ct);

    public async Task<IContainerEngine> ResolveAsync(bool requireMultiPlatform, CancellationToken ct = default)
    {
        if (requireMultiPlatform)
        {
            throw new InvalidOperationException(
                "multi-platform builds are not supported against Podman by this tool — use Docker with buildx, or build per-architecture images separately.");
        }

        var candidates = ResolveCandidateEndpoints();
        Exception? lastError = null;

        foreach (var endpoint in candidates)
        {
            DockerClient? client = null;
            try
            {
                client = new DockerClientConfiguration(endpoint).CreateClient();
                await client.System.PingAsync(ct);
                return new PodmanEngine(new DockerEngine(client));
            }
            catch (Exception ex)
            {
                client?.Dispose();
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"Could not reach a Podman API socket (tried: {string.Join(", ", candidates)}). " +
            "Ensure `systemctl --user enable --now podman.socket` (Linux) or `podman machine start` (Windows/macOS) has been run, " +
            "or set CONTAINER_HOST explicitly.",
            lastError);
    }

    private static IReadOnlyList<Uri> ResolveCandidateEndpoints()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CONTAINER_HOST");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return [new Uri(fromEnv)];
        }

        if (OperatingSystem.IsWindows())
        {
            return [new Uri("npipe://./pipe/podman-machine-default")];
        }

        var candidates = new List<Uri> { new("unix:///run/podman/podman.sock") };
        var uid = Environment.GetEnvironmentVariable("UID");
        if (!string.IsNullOrEmpty(uid))
        {
            candidates.Insert(0, new Uri($"unix:///run/user/{uid}/podman/podman.sock"));
        }
        return candidates;
    }
}
