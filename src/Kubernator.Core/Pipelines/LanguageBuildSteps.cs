using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines;

internal sealed record LanguageStep(string Name, string Run);

internal sealed record LanguageProfile
{
    public required string SetupAction { get; init; }
    public required IReadOnlyList<LanguageStep> Build { get; init; }
    public required IReadOnlyList<LanguageStep> Test { get; init; }
    public required string PublishOutput { get; init; }
    public required string CachePath { get; init; }
    public required string CacheKey { get; init; }
}

internal static class LanguageBuildSteps
{
    public static LanguageProfile For(AppKind kind, PipelineOptions options) => kind switch
    {
        AppKind.DotNet => new LanguageProfile
        {
            SetupAction = "actions/setup-dotnet@v4",
            Build =
            [
                new("dotnet restore", "dotnet restore"),
                new("dotnet publish", $"dotnet publish -c Release -o {options.PublishPath} --no-restore")
            ],
            Test = [new("dotnet test", "dotnet test --no-restore --logger trx --collect:\"XPlat Code Coverage\"")],
            PublishOutput = options.PublishPath,
            CachePath = "~/.nuget/packages",
            CacheKey = "nuget"
        },
        AppKind.NodeJs => new LanguageProfile
        {
            SetupAction = "actions/setup-node@v4",
            Build =
            [
                new("npm ci", "npm ci"),
                new("npm run build", "npm run build --if-present")
            ],
            Test = [new("npm test", "npm test --if-present")],
            PublishOutput = options.PublishPath,
            CachePath = "~/.npm",
            CacheKey = "npm"
        },
        AppKind.Python => new LanguageProfile
        {
            SetupAction = "actions/setup-python@v5",
            Build =
            [
                new("install deps", "pip install --no-cache-dir -r requirements.txt"),
                new("vendor deps for offline use", $"pip install --no-cache-dir --target ./vendor -r requirements.txt && cp -r ./vendor {options.PublishPath}/")
            ],
            Test = [new("pytest", "pytest --maxfail=1 -q || true")],
            PublishOutput = options.PublishPath,
            CachePath = "~/.cache/pip",
            CacheKey = "pip"
        },
        AppKind.Java => new LanguageProfile
        {
            SetupAction = "actions/setup-java@v4",
            Build = [new("maven package", "mvn -B -DskipTests=false package")],
            Test = [new("maven test", "mvn -B test")],
            PublishOutput = "target",
            CachePath = "~/.m2/repository",
            CacheKey = "maven"
        },
        AppKind.Go => new LanguageProfile
        {
            SetupAction = "actions/setup-go@v5",
            Build = [new("go build", $"CGO_ENABLED=0 go build -trimpath -ldflags=\"-s -w\" -o {options.PublishPath}/app ./...")],
            Test = [new("go test", "go test ./...")],
            PublishOutput = options.PublishPath,
            CachePath = "~/go/pkg/mod",
            CacheKey = "gomod"
        },
        AppKind.StaticWeb => new LanguageProfile
        {
            SetupAction = "actions/setup-node@v4",
            Build = [new("build static site", "if [ -f package.json ]; then npm ci && npm run build --if-present; fi")],
            Test = [],
            PublishOutput = options.PublishPath,
            CachePath = "~/.npm",
            CacheKey = "npm"
        },
        _ => throw new NotSupportedException($"No pipeline profile for {kind}")
    };
}
