using Kubernator.Core.Models;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Tests.Strategy;

public sealed class StrategySelectorTests
{
    private readonly StrategySelector selector = new();

    [Fact]
    public void AspNet_app_uses_chiseled_aspnet_base_with_extras_when_icu_required()
    {
        var app = SampleApp.AspNet(requiresIcu: true);

        var plan = selector.Plan(app);

        plan.RuntimeImage.Registry.Should().Be(AllowedRegistries.Microsoft);
        plan.RuntimeImage.Repository.Should().Be("dotnet/aspnet");
        plan.RuntimeImage.Tag.Should().EndWith("noble-chiseled-extra");
        plan.RuntimeImage.DefaultUserId.Should().Be(1654);
        plan.Security.RunAsUser.Should().Be(1654);
        plan.Security.ReadOnlyRootFilesystem.Should().BeTrue();
        plan.Security.DroppedCapabilities.Should().Contain("ALL");
        plan.ExposedPorts.Should().Contain(8080);
        plan.Health.Should().NotBeNull();
        plan.Health!.Kind.Should().Be(HealthProbeKind.HttpGet);
    }

    [Fact]
    public void Console_app_uses_runtime_base_and_no_health_probe()
    {
        var app = SampleApp.Console();

        var plan = selector.Plan(app);

        plan.RuntimeImage.Repository.Should().Be("dotnet/runtime");
        plan.RuntimeImage.DefaultUserId.Should().Be(1654);
        plan.ExposedPorts.Should().BeEmpty();
        plan.Health.Should().BeNull();
    }

    [Fact]
    public void SelfContained_app_uses_chainguard_static()
    {
        var app = SampleApp.SelfContained();

        var plan = selector.Plan(app);

        plan.RuntimeImage.Registry.Should().Be(AllowedRegistries.Chainguard);
        plan.RuntimeImage.Repository.Should().Be("chainguard/static");
        plan.RuntimeImage.DefaultUserId.Should().Be(65532);
        plan.Security.RunAsUser.Should().Be(65532);
    }

    [Fact]
    public void Aspnet_app_only_makes_tmp_writable()
    {
        var plan = selector.Plan(SampleApp.AspNet());

        plan.Security.WritableMounts.Should().Equal("/tmp");
    }

    [Fact]
    public void Static_web_app_makes_nginx_runtime_paths_writable()
    {
        var plan = selector.Plan(SampleApp.StaticWeb());

        plan.Security.ReadOnlyRootFilesystem.Should().BeTrue();
        plan.Security.WritableMounts.Should().Contain(["/var/lib/nginx/logs", "/var/lib/nginx/tmp", "/run"]);
    }

    [Fact]
    public void Image_name_is_sanitized()
    {
        var app = SampleApp.AspNet() with
        {
            EntryPoint = new EntryPoint
            {
                Path = "/x",
                AssemblyName = "Foo.Bar.Web",
                StartupCommand = "dotnet",
                Arguments = ["Foo.Bar.Web.dll"]
            }
        };

        var plan = selector.Plan(app);

        plan.ImageName.Should().Be("foo.bar.web");
    }
}

internal static class SampleApp
{
    public static AppDescriptor AspNet(bool requiresIcu = true) => new()
    {
        SourcePath = "/tmp/publish",
        Kind = AppKind.DotNet,
        Flavor = AppFlavor.DotNetAspNetCore,
        Runtime = new RuntimeInfo
        {
            Name = ".NET",
            Version = "10.0.0",
            Tfm = "net10.0",
            FrameworkReferences = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"],
            PublishMode = PublishMode.FrameworkDependent
        },
        Network = new NetworkInfo
        {
            Ports = [8080],
            ListensHttp = true,
            RequiresIngress = true
        },
        Dependencies = new DependencyInfo { RequiresIcu = requiresIcu },
        EntryPoint = new EntryPoint
        {
            Path = "/tmp/publish/MyWebApp.dll",
            AssemblyName = "MyWebApp",
            StartupCommand = "dotnet",
            Arguments = ["MyWebApp.dll"]
        },
        DetectionConfidence = 1.0
    };

    public static AppDescriptor Console() => new()
    {
        SourcePath = "/tmp/console",
        Kind = AppKind.DotNet,
        Flavor = AppFlavor.DotNetConsole,
        Runtime = new RuntimeInfo
        {
            Name = ".NET",
            Version = "10.0.0",
            Tfm = "net10.0",
            FrameworkReferences = ["Microsoft.NETCore.App"],
            PublishMode = PublishMode.FrameworkDependent
        },
        EntryPoint = new EntryPoint
        {
            Path = "/tmp/console/MyConsole.dll",
            AssemblyName = "MyConsole",
            StartupCommand = "dotnet",
            Arguments = ["MyConsole.dll"]
        }
    };

    public static AppDescriptor SelfContained() => new()
    {
        SourcePath = "/tmp/sc",
        Kind = AppKind.DotNet,
        Flavor = AppFlavor.DotNetConsole,
        Runtime = new RuntimeInfo
        {
            Name = ".NET",
            Version = "10.0.0",
            Tfm = "net10.0",
            RuntimeIdentifier = "linux-x64",
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.X64,
            PublishMode = PublishMode.SelfContained
        },
        EntryPoint = new EntryPoint
        {
            Path = "/tmp/sc/myapp",
            AssemblyName = "myapp",
            StartupCommand = "./myapp",
            Arguments = []
        }
    };

    public static AppDescriptor StaticWeb() => new()
    {
        SourcePath = "/tmp/site",
        Kind = AppKind.StaticWeb,
        Flavor = AppFlavor.StaticHtml,
        Runtime = new RuntimeInfo
        {
            Name = "Nginx (static)",
            PublishMode = PublishMode.SelfContained
        },
        Network = new NetworkInfo
        {
            Ports = [8080],
            ListensHttp = true,
            RequiresIngress = true
        },
        EntryPoint = new EntryPoint
        {
            Path = "/tmp/site",
            AssemblyName = "site",
            StartupCommand = "nginx",
            Arguments = ["-g", "daemon off;"]
        }
    };
}
