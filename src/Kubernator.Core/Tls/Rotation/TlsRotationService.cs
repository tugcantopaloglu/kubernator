namespace Kubernator.Core.Tls.Rotation;

public sealed class TlsRotationService : ITlsRotationService
{
    public async Task<TlsRotationResult> GenerateAsync(TlsRotationOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.SecretName))
        {
            throw new InvalidOperationException("secret name is required");
        }
        if (string.IsNullOrWhiteSpace(options.Hostname))
        {
            throw new InvalidOperationException("hostname is required");
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var saName = options.ServiceAccountName ?? $"{options.SecretName}-rotator";
        var cronName = options.CronJobName ?? $"{options.SecretName}-rotate";

        var written = new List<string>();
        await Write(Path.Combine(options.OutputDirectory, "serviceaccount.yaml"),
            TlsRotationEmitter.ServiceAccountYaml(options, saName), written, ct);
        await Write(Path.Combine(options.OutputDirectory, "role.yaml"),
            TlsRotationEmitter.RoleYaml(options, saName), written, ct);
        await Write(Path.Combine(options.OutputDirectory, "rolebinding.yaml"),
            TlsRotationEmitter.RoleBindingYaml(options, saName), written, ct);
        await Write(Path.Combine(options.OutputDirectory, "cronjob.yaml"),
            TlsRotationEmitter.CronJobYaml(options, cronName, saName), written, ct);

        return new TlsRotationResult
        {
            OutputDirectory = options.OutputDirectory,
            WrittenFiles = written,
            ResolvedServiceAccountName = saName,
            ResolvedCronJobName = cronName
        };
    }

    private static async Task Write(string path, string content, List<string> written, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, content, ct);
        written.Add(path);
    }
}
