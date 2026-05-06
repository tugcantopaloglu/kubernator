using Kubernator.Cli.Commands;
using Kubernator.Cli.Infrastructure;
using Kubernator.Core.DependencyInjection;
using Kubernator.Core.Updates;
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
    config.SetApplicationVersion(KubernatorVersion.Current);
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

    config.AddCommand<PullCommand>("pull")
        .WithDescription("Pull container images from a registry and save them as transferable .tar files for offline use.")
        .WithExample("pull", "nginx:1.27", "-o", "./images")
        .WithExample("pull", "nginx:1.27", "redis:7.4", "--combined", "-o", "./images");

    config.AddCommand<RehostCommand>("rehost")
        .WithDescription("On an air-gapped host, load images from a bundle, retag them under a private registry, push, and rewrite manifest image refs.")
        .WithExample("rehost", "--bundle", "./images", "--registry", "registry.airgap.local:5000")
        .WithExample("rehost", "--bundle", "./images", "--registry", "registry.airgap.local:5000", "--namespace", "infra/mirror", "--manifests", "./k8s");

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

    config.AddCommand<TlsRotateCommand>("tls-rotate")
        .WithDescription("Generate ServiceAccount + Role + RoleBinding + CronJob to periodically rotate a self-signed TLS Secret.")
        .WithExample("tls-rotate", "tls-cert", "--hostname", "app.example.com")
        .WithExample("tls-rotate", "tls-cert", "--hostname", "app.example.com", "--namespace", "prod", "--schedule", "0 0 */60 * *");

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

    config.AddCommand<DeployCommand>("deploy")
        .WithDescription("Deploy generated manifests to a Kubernetes cluster via kubectl.")
        .WithExample("deploy", "./publish", "--context", "kind-kubernator-test", "--namespace", "demo")
        .WithExample("deploy", "./publish", "--context", "test-eu", "--dry-run")
        .WithExample("deploy", "./publish", "--context", "prod-eu", "--allow-production")
        .WithExample("deploy", "--list-contexts");

    config.AddCommand<MonitorCommand>("monitor")
        .WithDescription("Snapshot or watch cluster state: node health, pods, ingress, network policies, and live metrics.")
        .WithExample("monitor")
        .WithExample("monitor", "--context", "prod-eu", "--namespace", "shop")
        .WithExample("monitor", "--watch", "5", "--no-netpol");

    config.AddCommand<AuditCommand>("audit")
        .WithDescription("Audit a manifests directory against the kubernator secure baseline (AUD codes).")
        .WithExample("audit", "./publish/.kubernator/kubernetes")
        .WithExample("audit", "./publish/.kubernator/kubernetes", "--namespace", "shop", "--fail-on", "warning");

    config.AddCommand<VaultCommand>("vault")
        .WithDescription("Manage the local key & cert vault (list / import / remove).")
        .WithExample("vault", "list")
        .WithExample("vault", "import", "--name", "prod-cosign", "--kind", "private", "--from", "./cosign.key", "--encrypted")
        .WithExample("vault", "remove", "--id", "ab12cd34");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Probe the local environment (engine, kubectl, kind, vulndb, state directory).")
        .WithExample("doctor");

    config.AddCommand<VersionCommand>("version")
        .WithDescription("Print the kubernator version and platform identifier.")
        .WithExample("version");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Check for or apply a self-update from a release manifest URL or local path.")
        .WithExample("update", "check", "--source", "https://example.com/kubernator/release.json")
        .WithExample("update", "apply", "--source", "./release.json");

    config.AddCommand<WizardCommand>("wizard")
        .WithDescription("Run the interactive wizard.")
        .WithAlias("ui");
});

if (args.Length == 0)
{
    return await app.RunAsync(["wizard"]);
}

return await app.RunAsync(args);
