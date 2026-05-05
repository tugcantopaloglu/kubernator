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
        var workdir = options?.WorkingDirectory ?? DefaultWorkingDirectory(app.Kind);

        var ports = new List<int>(app.Network.Ports);
        var envBuilder = new Dictionary<string, string>(app.EnvironmentHints, StringComparer.Ordinal);

        if (app.Kind == AppKind.DotNet && app.Network.RequiresIngress)
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
            Exposure = options?.Exposure,
            Platforms = NormalizePlatforms(options?.Platforms, app),
            Notes = baseImage.Notes is null ? [] : [baseImage.Notes]
        };
    }

    private static IReadOnlyList<string> NormalizePlatforms(IReadOnlyList<string>? requested, AppDescriptor app)
    {
        if (requested is { Count: > 0 })
        {
            return requested.Select(NormalizePlatform).Distinct(StringComparer.Ordinal).ToArray();
        }
        var os = app.Runtime.TargetOs switch
        {
            TargetOs.Linux or TargetOs.Any => "linux",
            TargetOs.LinuxMusl => "linux",
            TargetOs.Windows => "windows",
            TargetOs.Osx => "darwin",
            _ => "linux"
        };
        var arch = app.Runtime.TargetArch switch
        {
            TargetArchitecture.X64 => "amd64",
            TargetArchitecture.Arm64 => "arm64",
            TargetArchitecture.X86 => "386",
            TargetArchitecture.Arm => "arm",
            _ => "amd64"
        };
        return [$"{os}/{arch}"];
    }

    private static string NormalizePlatform(string raw)
    {
        var trimmed = raw.Trim().ToLowerInvariant();
        return trimmed.Contains('/', StringComparison.Ordinal) ? trimmed : $"linux/{trimmed}";
    }

    private static (string Command, IReadOnlyList<string> Arguments) ResolveEntrypoint(AppDescriptor app, string workdir)
    {
        if (app.Kind == AppKind.DotNet)
        {
            return ResolveDotNetEntrypoint(app, workdir);
        }

        if (app.EntryPoint is null)
        {
            return (app.Kind switch
            {
                AppKind.NodeJs => "node",
                AppKind.Python => "python",
                AppKind.Java => "java",
                AppKind.Go => $"{workdir}/app",
                AppKind.StaticWeb => "nginx",
                _ => "sh"
            }, []);
        }

        return (app.EntryPoint.StartupCommand ?? "sh", app.EntryPoint.Arguments);
    }

    private static (string Command, IReadOnlyList<string> Arguments) ResolveDotNetEntrypoint(AppDescriptor app, string workdir)
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
        var raw = app.Runtime.Version;
        if (string.IsNullOrEmpty(raw))
        {
            return "0.1.0";
        }
        var sanitized = SanitizeImageTag(raw);
        return string.IsNullOrEmpty(sanitized) ? "0.1.0" : sanitized;
    }

    private static string SanitizeImageTag(string raw)
    {
        var chars = raw.Select(c =>
            char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '-').ToArray();
        var tag = new string(chars).TrimStart('.', '-').Trim();
        if (tag.Length > 128)
        {
            tag = tag[..128];
        }
        return tag.TrimEnd('.', '-');
    }

    private static string DefaultWorkingDirectory(AppKind kind) => kind switch
    {
        AppKind.StaticWeb => "/usr/share/nginx/html",
        _ => "/app"
    };

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
