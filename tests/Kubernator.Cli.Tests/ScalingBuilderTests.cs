using Kubernator.Cli.Infrastructure;

namespace Kubernator.Cli.Tests;

public class ScalingBuilderTests
{
    [Fact]
    public void AllNull_ReturnsNull()
    {
        var result = ScalingBuilder.Build(null, null, null, null, null, null, null, null);
        result.Should().BeNull();
    }

    [Fact]
    public void OnlyHpaMin_ReturnsOptions()
    {
        var result = ScalingBuilder.Build(2, null, null, null, null, null, null, null);
        result.Should().NotBeNull();
        result!.HpaMinReplicas.Should().Be(2);
        result.HpaMaxReplicas.Should().BeNull();
    }

    [Fact]
    public void OnlyPdbPercent_ReturnsOptions()
    {
        var result = ScalingBuilder.Build(null, null, null, null, null, null, "50%", null);
        result.Should().NotBeNull();
        result!.PdbMinAvailablePercent.Should().Be("50%");
    }

    [Fact]
    public void EmptyPdbPercentTreatedAsNull()
    {
        var result = ScalingBuilder.Build(null, null, null, null, null, null, "", "");
        result.Should().BeNull();
    }

    [Fact]
    public void FullHpaAndPdb_ProjectsAllValues()
    {
        var result = ScalingBuilder.Build(2, 5, 70, 80, 1, 2, "60%", "40%")!;
        result.HpaMinReplicas.Should().Be(2);
        result.HpaMaxReplicas.Should().Be(5);
        result.HpaTargetCpuUtilization.Should().Be(70);
        result.HpaTargetMemoryUtilization.Should().Be(80);
        result.PdbMinAvailable.Should().Be(1);
        result.PdbMaxUnavailable.Should().Be(2);
        result.PdbMinAvailablePercent.Should().Be("60%");
        result.PdbMaxUnavailablePercent.Should().Be("40%");
    }
}
