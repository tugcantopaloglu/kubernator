using Kubernator.Cli.Commands;

namespace Kubernator.Cli.Tests;

public class CommandSettingsTests
{
    [Fact]
    public void Generate_RequiresPath()
    {
        var settings = new GenerateCommand.Settings { Path = "" };
        var result = settings.Validate();
        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("path");
    }

    [Fact]
    public void Generate_AcceptsNonEmptyPath()
    {
        var settings = new GenerateCommand.Settings { Path = "/some/path" };
        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Deploy_RequiresPathOrManifestsOrListContexts()
    {
        var settings = new DeployCommand.Settings { Path = null };
        var result = settings.Validate();
        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("path");
    }

    [Fact]
    public void Deploy_ListContextsAlone_IsValid()
    {
        var settings = new DeployCommand.Settings { ListContexts = true };
        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Deploy_ManifestsDirOnly_IsValid()
    {
        var settings = new DeployCommand.Settings { ManifestsDirectory = "/tmp/manifests" };
        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Deploy_PathOnly_IsValid()
    {
        var settings = new DeployCommand.Settings { Path = "/some/path" };
        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }
}
