using Kubernator.Core.Containers;
using NSubstitute;

namespace Kubernator.Runtime.Tests;

public sealed class ContainerEngineSelectorTests : IDisposable
{
    private const string EnvVarName = "KUBERNATOR_CONTAINER_ENGINE";

    public ContainerEngineSelectorTests()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    private static IContainerEngineProvider FakeProvider(string kind)
    {
        var engine = Substitute.For<IContainerEngine>();
        engine.Kind.Returns(kind);
        var provider = Substitute.For<IContainerEngineProvider>();
        provider.ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(engine));
        return provider;
    }

    private static IContainerEngineProvider FailingProvider(string message)
    {
        var provider = Substitute.For<IContainerEngineProvider>();
        provider.ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<IContainerEngine>>(_ => throw new InvalidOperationException(message));
        return provider;
    }

    [Fact]
    public async Task Explicit_docker_preference_only_calls_the_docker_provider()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "docker");
        var docker = FakeProvider("docker");
        var podman = FakeProvider("podman");
        var sut = new ContainerEngineSelector(docker, podman);

        var engine = await sut.ResolveAsync();

        engine.Kind.Should().Be("docker");
        await podman.DidNotReceive().ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Explicit_podman_preference_only_calls_the_podman_provider()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "podman");
        var docker = FakeProvider("docker");
        var podman = FakeProvider("podman");
        var sut = new ContainerEngineSelector(docker, podman);

        var engine = await sut.ResolveAsync();

        engine.Kind.Should().Be("podman");
        await docker.DidNotReceive().ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    public async Task Auto_or_unset_preference_prefers_docker_when_it_is_reachable(string? preference)
    {
        Environment.SetEnvironmentVariable(EnvVarName, preference);
        var docker = FakeProvider("docker");
        var podman = FakeProvider("podman");
        var sut = new ContainerEngineSelector(docker, podman);

        var engine = await sut.ResolveAsync();

        engine.Kind.Should().Be("docker");
        await podman.DidNotReceive().ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auto_preference_falls_back_to_podman_when_docker_is_unreachable()
    {
        var docker = FailingProvider("no docker socket");
        var podman = FakeProvider("podman");
        var sut = new ContainerEngineSelector(docker, podman);

        var engine = await sut.ResolveAsync();

        engine.Kind.Should().Be("podman");
    }

    [Fact]
    public async Task Auto_preference_throws_a_combined_error_when_neither_engine_is_reachable()
    {
        var docker = FailingProvider("no docker socket");
        var podman = FailingProvider("no podman socket");
        var sut = new ContainerEngineSelector(docker, podman);

        var act = () => sut.ResolveAsync();

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("no docker socket");
        ex.Which.Message.Should().Contain("no podman socket");
    }

    [Fact]
    public async Task Unknown_preference_throws()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "not-a-real-engine");
        var sut = new ContainerEngineSelector(FakeProvider("docker"), FakeProvider("podman"));

        var act = () => sut.ResolveAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not-a-real-engine*");
    }
}
