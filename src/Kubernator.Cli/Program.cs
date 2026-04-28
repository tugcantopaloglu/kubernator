using Kubernator.Cli.Commands;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.DependencyInjection;
using Kubernator.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

var services = new ServiceCollection();
services.AddLogging(b =>
{
    b.SetMinimumLevel(LogLevel.Warning);
    b.AddProvider(new SpectreLoggerProvider());
});
services.AddKubernatorCore();
services.AddKubernatorRuntime();

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("kubernator");
    config.SetApplicationVersion("0.1.0");
    config.PropagateExceptions();

    config.AddCommand<AnalyzeCommand>("analyze")
        .WithDescription("Detect application type and report runtime, dependencies, and network surface.")
        .WithExample("analyze", "./publish")
        .WithExample("analyze", "./publish", "--json");

    config.AddCommand<GenerateCommand>("generate")
        .WithDescription("Generate Dockerfile, .dockerignore, and Kubernetes manifests for the application.")
        .WithExample("generate", "./publish")
        .WithExample("generate", "./publish", "--namespace", "myapp", "--replicas", "3");

    config.AddCommand<BuildCommand>("build")
        .WithDescription("Generate files and build the container image via Docker or compatible engine.")
        .WithExample("build", "./publish")
        .WithExample("build", "./publish", "--name", "myapp", "--tag", "1.0.0");

    config.AddCommand<BundleCommand>("bundle")
        .WithDescription("Build the image and pack everything into an air-gapped .kubpack bundle.")
        .WithExample("bundle", "./publish", "-o", "./out/myapp.kubpack")
        .WithExample("bundle", "./publish", "--namespace", "prod", "--replicas", "3");

    config.AddCommand<VerifyCommand>("verify")
        .WithDescription("Verify the integrity (and optional cosign signature) of a .kubpack bundle.")
        .WithExample("verify", "./out/myapp.kubpack")
        .WithExample("verify", "./out/myapp.kubpack", "--require-signature");

    config.AddCommand<KeygenCommand>("keygen")
        .WithDescription("Generate a cosign-compatible ECDSA P-256 key pair.")
        .WithExample("keygen", "-o", "./keys");

    config.AddCommand<SignCommand>("sign")
        .WithDescription("Sign a bundle with a private key, producing a detached cosign-compatible signature.")
        .WithExample("sign", "./out/myapp.kubpack", "--key", "./keys/cosign.key");

    config.AddCommand<WizardCommand>("wizard")
        .WithDescription("Run the interactive wizard.")
        .WithAlias("ui");
});

if (args.Length == 0)
{
    return await app.RunAsync(["wizard"]);
}

return await app.RunAsync(args);
