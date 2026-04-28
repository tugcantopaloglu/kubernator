using Kubernator.Core.Models;

namespace Kubernator.Core.Strategy;

public sealed record StrategyOptions
{
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public string? WorkingDirectory { get; init; }
}

public interface IStrategySelector
{
    BuildPlan Plan(AppDescriptor app, StrategyOptions? options = null);
}
