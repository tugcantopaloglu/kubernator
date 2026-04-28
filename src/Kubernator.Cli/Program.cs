using Kubernator.Cli.Commands;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.DependencyInjection;
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

    config.AddCommand<WizardCommand>("wizard")
        .WithDescription("Run the interactive wizard.")
        .WithAlias("ui");
});

if (args.Length == 0)
{
    return await app.RunAsync(["wizard"]);
}

return await app.RunAsync(args);
