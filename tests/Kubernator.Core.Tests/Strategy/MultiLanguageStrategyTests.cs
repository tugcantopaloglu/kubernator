using Kubernator.Core.Models;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Tests.Strategy;

public sealed class MultiLanguageStrategyTests
{
    private readonly StrategySelector selector = new();

    [Fact]
    public void Node_app_uses_chainguard_node_with_npm_start()
    {
        var app = new AppDescriptor
        {
            SourcePath = "/tmp",
            Kind = AppKind.NodeJs,
            Flavor = AppFlavor.NodeExpress,
            Runtime = new RuntimeInfo { Name = "Node.js", Tfm = ">=20" },
            Network = new NetworkInfo { Ports = [3000], ListensHttp = true, RequiresIngress = true },
            EntryPoint = new EntryPoint
            {
                Path = "/tmp",
                AssemblyName = "demo",
                StartupCommand = "npm",
                Arguments = ["start", "--silent"]
            }
        };

        var plan = selector.Plan(app);

        plan.RuntimeImage.Registry.Should().Be(AllowedRegistries.Chainguard);
        plan.RuntimeImage.Repository.Should().Be("chainguard/node");
        plan.EntrypointCommand.Should().Be("npm");
        plan.EntrypointArguments.Should().ContainInOrder("start", "--silent");
        plan.Security.RunAsUser.Should().Be(65532);
    }

    [Fact]
    public void Java_spring_boot_uses_chainguard_jre_with_java_jar()
    {
        var app = new AppDescriptor
        {
            SourcePath = "/tmp",
            Kind = AppKind.Java,
            Flavor = AppFlavor.JavaSpringBoot,
            Runtime = new RuntimeInfo { Name = "Java" },
            Network = new NetworkInfo { Ports = [9090], ListensHttp = true, RequiresIngress = true },
            EntryPoint = new EntryPoint
            {
                Path = "/tmp/demo.jar",
                AssemblyName = "demo",
                StartupCommand = "java",
                Arguments = ["-jar", "demo.jar"]
            }
        };

        var plan = selector.Plan(app);

        plan.RuntimeImage.Repository.Should().Be("chainguard/jre");
        plan.EntrypointCommand.Should().Be("java");
        plan.EntrypointArguments.Should().ContainInOrder("-jar", "demo.jar");
    }

    [Fact]
    public void Static_web_uses_nginx_workdir()
    {
        var app = new AppDescriptor
        {
            SourcePath = "/tmp",
            Kind = AppKind.StaticWeb,
            Flavor = AppFlavor.StaticSpa,
            Runtime = new RuntimeInfo { Name = "nginx" },
            Network = new NetworkInfo { Ports = [8080], ListensHttp = true, RequiresIngress = true },
            EntryPoint = new EntryPoint
            {
                Path = "/tmp",
                AssemblyName = "site",
                StartupCommand = "nginx",
                Arguments = ["-g", "daemon off;"]
            }
        };

        var plan = selector.Plan(app);

        plan.RuntimeImage.Repository.Should().Be("chainguard/nginx");
        plan.WorkingDirectory.Should().Be("/usr/share/nginx/html");
    }

    [Fact]
    public void Go_static_uses_chainguard_static()
    {
        var app = new AppDescriptor
        {
            SourcePath = "/tmp",
            Kind = AppKind.Go,
            Flavor = AppFlavor.GoBinary,
            Runtime = new RuntimeInfo { Name = "Go", PublishMode = PublishMode.SelfContained },
            EntryPoint = new EntryPoint
            {
                Path = "/tmp/myapp",
                AssemblyName = "myapp",
                StartupCommand = "/app/myapp",
                Arguments = []
            }
        };

        var plan = selector.Plan(app);

        plan.RuntimeImage.Repository.Should().Be("chainguard/static");
        plan.EntrypointCommand.Should().Be("/app/myapp");
    }

    [Fact]
    public void Python_fastapi_uses_chainguard_python()
    {
        var app = new AppDescriptor
        {
            SourcePath = "/tmp",
            Kind = AppKind.Python,
            Flavor = AppFlavor.PythonFastApi,
            Runtime = new RuntimeInfo { Name = "Python" },
            Network = new NetworkInfo { Ports = [8000], ListensHttp = true, RequiresIngress = true },
            EntryPoint = new EntryPoint
            {
                Path = "/tmp",
                AssemblyName = "main",
                StartupCommand = "uvicorn",
                Arguments = ["main:app", "--host", "0.0.0.0", "--port", "8000"]
            }
        };

        var plan = selector.Plan(app);

        plan.RuntimeImage.Repository.Should().Be("chainguard/python");
        plan.EntrypointCommand.Should().Be("uvicorn");
    }
}
