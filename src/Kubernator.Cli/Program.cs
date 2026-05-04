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

    config.AddCommand<PipelineCommand>("pipeline")
        .WithDescription("Generate a CI/CD pipeline (GitHub Actions, GitLab CI, Azure DevOps, or Tekton).")
        .WithExample("pipeline", "./publish", "--target", "gh-actions")
        .WithExample("pipeline", "./publish", "--target", "tekton", "-o", ".");

    config.AddCommand<HelmCommand>("helm")
        .WithDescription("Generate a Helm chart with parameterized templates.")
        .WithExample("helm", "./publish", "-o", "./charts")
        .WithExample("helm", "./publish", "--package", "--hostname", "app.example.com", "--tls", "self-signed");

    config.AddCommand<KustomizeCommand>("kustomize")
        .WithDescription("Generate a Kustomize base + overlays for production / staging / dev.")
        .WithExample("kustomize", "./publish", "-o", "./kustomize")
        .WithExample("kustomize", "./publish", "--overlay", "production", "--overlay", "staging");

    config.AddCommand<GitOpsCommand>("gitops")
        .WithDescription("Generate Argo CD Application + AppProject CRs for GitOps continuous delivery.")
        .WithExample("gitops", "./publish", "--repo-url", "https://git.example.com/org/repo")
        .WithExample("gitops", "./publish", "--repo-url", "https://git.example.com/org/repo", "--source-kind", "helm", "--source-path", "charts/app");

    config.AddCommand<VulnDbCommand>("vulndb")
        .WithDescription("Manage the offline vulnerability database (status / update / import-zip / import / export).")
        .WithExample("vulndb", "update")
        .WithExample("vulndb", "update", "--ecosystem", "NuGet")
        .WithExample("vulndb", "import-zip", "--ecosystem", "NuGet", "--zip", "./NuGet.zip")
        .WithExample("vulndb", "export", "--bundle", "./vulndb.tar.gz")
        .WithExample("vulndb", "import", "--bundle", "./vulndb.tar.gz");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan an application's dependencies against the local vulnerability database.")
        .WithExample("scan", "./publish")
        .WithExample("scan", "./publish", "--min-severity", "high", "--fail-on", "critical");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate the build end-to-end on a local kind cluster (requires kind + kubectl on PATH).")
        .WithExample("validate", "./publish")
        .WithExample("validate", "./publish", "--probe-path", "/health", "--keep-cluster");

    config.AddCommand<WizardCommand>("wizard")
        .WithDescription("Run the interactive wizard.")
        .WithAlias("ui");
});

if (args.Length == 0)
{
    return await app.RunAsync(["wizard"]);
}

return await app.RunAsync(args);
