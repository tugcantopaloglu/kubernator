using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tls.Rotation;

namespace Kubernator.Core.Tests.Tls;

public sealed class TlsRotationServiceTests
{
    private readonly TlsRotationService service = new();

    [Fact]
    public async Task Writes_serviceaccount_role_rolebinding_and_cronjob()
    {
        using var temp = TempPublishOutput.Create();

        var result = await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "tls-cert",
            Hostname = "app.example.com"
        });

        File.Exists(Path.Combine(temp.Path, "serviceaccount.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "role.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "rolebinding.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "cronjob.yaml")).Should().BeTrue();
        result.WrittenFiles.Should().HaveCount(4);
        result.ResolvedServiceAccountName.Should().Be("tls-cert-rotator");
        result.ResolvedCronJobName.Should().Be("tls-cert-rotate");
    }

    [Fact]
    public async Task Role_grants_secret_get_update_patch_on_target_secret_only()
    {
        using var temp = TempPublishOutput.Create();
        await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "tls-cert",
            Hostname = "app.example.com"
        });

        var role = await File.ReadAllTextAsync(Path.Combine(temp.Path, "role.yaml"));
        role.Should().Contain("kind: Role");
        role.Should().Contain("resources: [\"secrets\"]");
        role.Should().Contain("resourceNames: [tls-cert]");
        role.Should().Contain("verbs: [\"get\", \"update\", \"patch\"]");
    }

    [Fact]
    public async Task CronJob_uses_specified_schedule_image_and_namespace()
    {
        using var temp = TempPublishOutput.Create();
        await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "tls-cert",
            Hostname = "app.example.com",
            Namespace = "prod",
            Schedule = "0 0 */60 * *",
            Image = "cgr.dev/chainguard/wolfi-base:1.2.3"
        });

        var cron = await File.ReadAllTextAsync(Path.Combine(temp.Path, "cronjob.yaml"));
        cron.Should().Contain("kind: CronJob");
        cron.Should().Contain("namespace: prod");
        cron.Should().Contain("schedule: \"0 0 */60 * *\"");
        cron.Should().Contain("image: \"cgr.dev/chainguard/wolfi-base:1.2.3\"");
        cron.Should().Contain("runAsNonRoot: true");
        cron.Should().Contain("readOnlyRootFilesystem: true");
        cron.Should().Contain("drop: [\"ALL\"]");
        cron.Should().Contain("automountServiceAccountToken: true");
        cron.Should().Contain("openssl req -x509");
    }

    [Fact]
    public async Task CronJob_includes_extra_hostnames_in_san()
    {
        using var temp = TempPublishOutput.Create();
        await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "tls-cert",
            Hostname = "app.example.com",
            AdditionalHostnames = ["api.example.com", "admin.example.com"]
        });

        var cron = await File.ReadAllTextAsync(Path.Combine(temp.Path, "cronjob.yaml"));
        cron.Should().Contain("app.example.com,api.example.com,admin.example.com");
    }

    [Fact]
    public async Task Throws_when_secret_or_hostname_missing()
    {
        using var temp = TempPublishOutput.Create();

        var noSecret = async () => await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "",
            Hostname = "app.example.com"
        });
        await noSecret.Should().ThrowAsync<InvalidOperationException>();

        var noHost = async () => await service.GenerateAsync(new TlsRotationOptions
        {
            OutputDirectory = temp.Path,
            SecretName = "tls-cert",
            Hostname = ""
        });
        await noHost.Should().ThrowAsync<InvalidOperationException>();
    }
}
