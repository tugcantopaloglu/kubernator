using Kubernator.Core.Models;

namespace Kubernator.Core.Strategy;

public sealed class StrategySelector : IStrategySelector
{
    public BuildPlan Plan(AppDescriptor app, StrategyOptions? options = null)
    {
        var baseImage = BaseImageCatalog.SelectFor(app);
        if (!AllowedRegistries.IsAllowed(baseImage.Registry))
        {
            throw new InvalidOperationException($"Selected registry {baseImage.Registry} is not in the allowlist.");
        }

        var imageName = options?.ImageName ?? DeriveImageName(app);
        var imageTag = options?.ImageTag ?? DeriveImageTag(app);
        var workdir = options?.WorkingDirectory ?? "/app";

        var ports = new List<int>(app.Network.Ports);
        var envBuilder = new Dictionary<string, string>(app.EnvironmentHints, StringComparer.Ordinal);

        if (app.Network.RequiresIngress)
        {
            envBuilder["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
        }

        var entrypoint = ResolveEntrypoint(app, workdir);

        var health = ResolveHealthProbe(app);

        return new BuildPlan
        {
            App = app,
            RuntimeImage = baseImage,
            BuildImage = null,
            Strategy = BuildStrategy.CopyFromPublish,
            ImageName = imageName,
            ImageTag = imageTag,
            WorkingDirectory = workdir,
            ExposedPorts = ports,
            EnvironmentVariables = envBuilder,
            EntrypointCommand = entrypoint.Command,
            EntrypointArguments = entrypoint.Arguments,
            Health = health,
            Security = new SecurityHardening
            {
                RunAsUser = baseImage.DefaultUserId,
                RunAsGroup = baseImage.DefaultGroupId
            },
            Notes = baseImage.Notes is null ? [] : [baseImage.Notes]
        };
    }

    private static (string Command, IReadOnlyList<string> Arguments) ResolveEntrypoint(AppDescriptor app, string workdir)
    {
        if (app.EntryPoint is null)
        {
            return ("dotnet", []);
        }

        if (app.Runtime.PublishMode is PublishMode.SelfContained or PublishMode.NativeAot &&
            !string.IsNullOrEmpty(app.EntryPoint.AssemblyName))
        {
            var binary = $"{app.EntryPoint.AssemblyName}";
            return ($"{workdir}/{binary}", []);
        }

        var dll = app.EntryPoint.Arguments.Count > 0
            ? app.EntryPoint.Arguments[0]
            : $"{app.EntryPoint.AssemblyName}.dll";
        return ("dotnet", [$"{workdir}/{dll}"]);
    }

    private static HealthProbe? ResolveHealthProbe(AppDescriptor app)
    {
        if (!app.Network.RequiresIngress || app.Network.Ports.Count == 0)
        {
            return null;
        }

        var port = app.Network.Ports[0];
        return new HealthProbe
        {
            Kind = HealthProbeKind.HttpGet,
            HttpPath = "/health",
            Port = port
        };
    }

    private static string DeriveImageName(AppDescriptor app)
    {
        var name = app.EntryPoint?.AssemblyName ?? Path.GetFileName(app.SourcePath);
        return SanitizeImageName(name ?? "app");
    }

    private static string DeriveImageTag(AppDescriptor app)
    {
        return app.Runtime.Version ?? "0.1.0";
    }

    private static string SanitizeImageName(string raw)
    {
        var lowered = raw.ToLowerInvariant();
        var chars = lowered.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '/'
                ? c
                : '-').ToArray();
        var name = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrEmpty(name) ? "app" : name;
    }
}
