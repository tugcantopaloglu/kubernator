using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Generation;
using Kubernator.Core.Models;
using Kubernator.Core.Strategy;
using Kubernator.Web.Services;
using NSubstitute;

namespace Kubernator.Web.Tests.Services;

public sealed class BuildPipelineTests : IDisposable
{
    private readonly string sourceDir;

    private readonly IAnalysisService analysis = Substitute.For<IAnalysisService>();
    private readonly IStrategySelector strategy = Substitute.For<IStrategySelector>();
    private readonly IGenerationService generation = Substitute.For<IGenerationService>();
    private readonly IContainerEngineProvider engineProvider = Substitute.For<IContainerEngineProvider>();

    public BuildPipelineTests()
    {
        sourceDir = Path.Combine(Path.GetTempPath(), $"buildpipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "Program.cs"), "// entry point");
    }

    public void Dispose()
    {
        try { Directory.Delete(sourceDir, recursive: true); } catch { }
    }

    private BuildPipeline CreateSut() => new(analysis, strategy, generation, engineProvider);

    private static AppDescriptor SomeAppDescriptor(string path) => new()
    {
        SourcePath = path,
        Runtime = new RuntimeInfo { Name = "dotnet" }
    };

    private static BuildPlan SomePlan(AppDescriptor app, string imageName = "myapp", string imageTag = "latest") => new()
    {
        App = app,
        RuntimeImage = new BaseImage
        {
            Registry = "mcr.microsoft.com",
            Repository = "dotnet/runtime",
            Tag = "8.0",
            DisplayName = ".NET 8 runtime",
            NonRootByDefault = true,
            DefaultUserId = 1000,
            DefaultGroupId = 1000
        },
        Strategy = BuildStrategy.CopyFromPublish,
        ImageName = imageName,
        ImageTag = imageTag,
        WorkingDirectory = "/app",
        ExposedPorts = [8080],
        EnvironmentVariables = new Dictionary<string, string>(),
        EntrypointCommand = "/app/entry",
        EntrypointArguments = [],
        Health = null,
        Security = new SecurityHardening()
    };

    /// <summary>Stubs generation.GenerateAsync to actually write a Dockerfile (and optionally
    /// .dockerignore) under the requested output directory, mirroring what the real
    /// generation service does — BuildPipeline copies those files off disk afterwards.</summary>
    private void StubGeneration(BuildPlan plan, IReadOnlyList<string> writtenFiles, bool writeDockerIgnore = false)
    {
        generation.GenerateAsync(plan, Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var options = callInfo.Arg<GenerationOptions>();
                Directory.CreateDirectory(options.OutputDirectory);
                File.WriteAllText(Path.Combine(options.OutputDirectory, "Dockerfile"), "FROM scratch");
                if (writeDockerIgnore)
                {
                    File.WriteAllText(Path.Combine(options.OutputDirectory, ".dockerignore"), "bin/\nobj/");
                }
                return Task.FromResult(new GenerationResult
                {
                    OutputDirectory = options.OutputDirectory,
                    WrittenFiles = writtenFiles
                });
            });
    }

    private static async IAsyncEnumerable<string> Lines(params string[] lines)
    {
        foreach (var line in lines)
        {
            yield return line;
        }
        await Task.CompletedTask;
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];
        public void Report(string value) => Messages.Add(value);
    }

    [Fact]
    public async Task NoBuild_stops_after_generation_and_never_touches_the_container_engine()
    {
        var app = SomeAppDescriptor(sourceDir);
        var plan = SomePlan(app);
        analysis.AnalyzeAsync(sourceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult(app));
        strategy.Plan(app, Arg.Any<StrategyOptions>()).Returns(plan);
        StubGeneration(plan, ["Dockerfile", "k8s/deployment.yaml"]);

        var progress = new RecordingProgress();
        var sut = CreateSut();

        var result = await sut.RunAsync(new BuildPipelineRequest { Path = sourceDir, NoBuild = true }, progress);

        result.OutputDirectory.Should().Be(Path.Combine(sourceDir, ".kubernator"));
        result.GeneratedFileCount.Should().Be(2);
        result.ImageReference.Should().BeNull();
        result.EngineName.Should().BeNull();
        result.ImageSizeBytes.Should().BeNull();

        await engineProvider.DidNotReceive().ResolveAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        progress.Messages.Should().Equal(
            $"analyzing {sourceDir}",
            $"generating into {Path.Combine(sourceDir, ".kubernator")}",
            "generated 2 file(s)");
    }

    [Fact]
    public async Task Full_build_stages_source_and_generated_dockerfile_while_excluding_the_output_directory_itself()
    {
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        File.WriteAllText(Path.Combine(sourceDir, "sub", "nested.txt"), "nested");

        var app = SomeAppDescriptor(sourceDir);
        var plan = SomePlan(app);
        analysis.AnalyzeAsync(sourceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult(app));
        strategy.Plan(app, Arg.Any<StrategyOptions>()).Returns(plan);
        StubGeneration(plan, ["Dockerfile", "k8s/deployment.yaml"], writeDockerIgnore: true);

        var engine = Substitute.For<IContainerEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EngineInfo
        {
            Name = "docker",
            Version = "24.0.7",
            ApiVersion = "1.43",
            OperatingSystem = "linux",
            Architecture = "amd64"
        }));
        engine.BuildAsync(Arg.Any<BuildContext>(), Arg.Any<CancellationToken>()).Returns(Lines("step 1/3", "step 3/3 done"));
        engine.GetImageAsync(plan.FullImageReference, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ImageInfo?>(new ImageInfo
        {
            Id = "sha256:abc123",
            Tags = [plan.FullImageReference],
            SizeBytes = 123_456,
            CreatedAt = DateTimeOffset.UtcNow
        }));
        engineProvider.ResolveAsync(false, Arg.Any<CancellationToken>()).Returns(Task.FromResult(engine));

        var progress = new RecordingProgress();
        var sut = CreateSut();

        var result = await sut.RunAsync(new BuildPipelineRequest { Path = sourceDir, NoBuild = false }, progress);

        result.ImageReference.Should().Be("myapp:latest");
        result.ImageSizeBytes.Should().Be(123_456);
        result.EngineName.Should().Be("docker");
        result.EngineVersion.Should().Be("24.0.7");

        var output = Path.Combine(sourceDir, ".kubernator");
        var staging = Path.Combine(output, "build-context");
        File.Exists(Path.Combine(staging, "Program.cs")).Should().BeTrue("source files should be copied into the build context");
        File.Exists(Path.Combine(staging, "sub", "nested.txt")).Should().BeTrue("nested source files should be copied too");
        File.Exists(Path.Combine(staging, "Dockerfile")).Should().BeTrue("the generated Dockerfile should be copied into the build context");
        File.Exists(Path.Combine(staging, ".dockerignore")).Should().BeTrue();
        Directory.Exists(Path.Combine(staging, ".kubernator")).Should().BeFalse(
            "the output directory lives under the source path and must be excluded from its own build context, or copying would recurse into itself");

        engine.Received(1).BuildAsync(
            Arg.Is<BuildContext>(c =>
                c.ContextDirectory == staging &&
                c.DockerfilePath == Path.Combine(staging, "Dockerfile") &&
                c.ImageName == "myapp" &&
                c.ImageTag == "latest" &&
                c.Platforms.Count == 0),
            Arg.Any<CancellationToken>());

        progress.Messages.Should().Contain("step 1/3");
        progress.Messages.Should().Contain("step 3/3 done");
        progress.Messages.Should().Contain("engine docker 24.0.7 (linux/amd64)");
        progress.Messages.Should().Contain($"building {plan.FullImageReference}");
    }

    [Fact]
    public async Task Multi_platform_request_against_an_engine_without_support_throws_and_never_builds()
    {
        var app = SomeAppDescriptor(sourceDir);
        var plan = SomePlan(app);
        analysis.AnalyzeAsync(sourceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult(app));
        strategy.Plan(app, Arg.Any<StrategyOptions>()).Returns(plan);
        StubGeneration(plan, ["Dockerfile"]);

        var engine = Substitute.For<IContainerEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EngineInfo
        {
            Name = "docker",
            Version = "24.0.7",
            ApiVersion = "1.43",
            OperatingSystem = "linux",
            Architecture = "amd64"
        }));
        // SupportsMultiPlatform defaults to false and is deliberately left unstubbed.
        engineProvider.ResolveAsync(true, Arg.Any<CancellationToken>()).Returns(Task.FromResult(engine));

        var sut = CreateSut();
        var request = new BuildPipelineRequest { Path = sourceDir, NoBuild = false, Platforms = ["linux/amd64", "linux/arm64"] };

        var act = () => sut.RunAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*multi-platform*");
        engine.DidNotReceive().BuildAsync(Arg.Any<BuildContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Custom_output_directory_outside_the_source_path_copies_the_whole_source_tree_unfiltered()
    {
        var app = SomeAppDescriptor(sourceDir);
        var plan = SomePlan(app);
        analysis.AnalyzeAsync(sourceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult(app));
        strategy.Plan(app, Arg.Any<StrategyOptions>()).Returns(plan);
        StubGeneration(plan, ["Dockerfile"]);

        var engine = Substitute.For<IContainerEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EngineInfo
        {
            Name = "docker",
            Version = "24.0.7",
            ApiVersion = "1.43",
            OperatingSystem = "linux",
            Architecture = "amd64"
        }));
        engine.BuildAsync(Arg.Any<BuildContext>(), Arg.Any<CancellationToken>()).Returns(Lines());
        engine.GetImageAsync(plan.FullImageReference, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ImageInfo?>(null));
        engineProvider.ResolveAsync(false, Arg.Any<CancellationToken>()).Returns(Task.FromResult(engine));

        var outsideOutput = Path.Combine(Path.GetTempPath(), $"buildpipeline-out-{Guid.NewGuid():N}");
        try
        {
            var sut = CreateSut();
            var result = await sut.RunAsync(new BuildPipelineRequest { Path = sourceDir, OutputDirectory = outsideOutput, NoBuild = false });

            result.ImageSizeBytes.Should().BeNull("GetImageAsync returned null, e.g. the engine could not locate the built image");
            var staging = Path.Combine(outsideOutput, "build-context");
            File.Exists(Path.Combine(staging, "Program.cs")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(outsideOutput, recursive: true); } catch { }
        }
    }
}
