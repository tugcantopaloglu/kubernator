using Kubernator.Core.Abstractions;
using Kubernator.Core.Detection.DotNet;
using Kubernator.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kubernator.Core.Analysis.DotNet;

public sealed class DotNetAnalyzer : IAppAnalyzer
{
    private readonly ILogger<DotNetAnalyzer> logger;

    public DotNetAnalyzer(ILogger<DotNetAnalyzer> logger)
    {
        this.logger = logger;
    }

    public AppKind Handles => AppKind.DotNet;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        var path = detection.SourcePath;
        var layouts = DotNetLayoutScanner.Scan(path);
        if (layouts.Count == 0)
        {
            return Task.FromResult(AnalyzeSourceOnly(detection));
        }

        var layout = layouts.FirstOrDefault(l => l.HasRuntimeConfig && l.HasMainAssembly)
                     ?? layouts.FirstOrDefault(l => l.HasRuntimeConfig)
                     ?? layouts[0];

        return Task.FromResult(AnalyzePublished(detection, layout, ct));
    }

    private AppDescriptor AnalyzePublished(DetectionResult detection, DotNetPublishLayout layout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        RuntimeConfig? runtimeConfig = null;
        if (layout.HasRuntimeConfig)
        {
            try
            {
                runtimeConfig = RuntimeConfigReader.Read(layout.RuntimeConfigPath!);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read runtime config");
            }
        }

        DepsJsonModel depsModel;
        try
        {
            depsModel = DepsJsonReader.Read(layout.DepsJsonPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read deps.json");
            depsModel = new DepsJsonModel();
        }

        AssemblyMetadata? assembly = null;
        if (layout.HasMainAssembly)
        {
            try
            {
                assembly = AssemblyMetadataReader.Read(layout.MainAssemblyPath!);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read assembly metadata");
            }
        }

        var hints = AppSettingsScanner.Scan(layout.RootPath);
        var warnings = new List<string>(detection.Warnings);

        var frameworks = runtimeConfig?.Frameworks ?? [];
        var aspNet = frameworks.Any(f => f.Name.Equals("Microsoft.AspNetCore.App", StringComparison.Ordinal))
                     || (assembly?.ReferencedAssemblies.Any(IsAspNetAssembly) ?? false);
        var grpc = assembly?.ReferencedAssemblies.Any(r =>
            r.StartsWith("Grpc.", StringComparison.Ordinal) ||
            r.Equals("Grpc.AspNetCore", StringComparison.Ordinal)) ?? false;
        var blazorServer = assembly?.ReferencedAssemblies.Any(r =>
            r.Equals("Microsoft.AspNetCore.Components.Server", StringComparison.Ordinal)) ?? false;
        var workerLike = assembly?.ReferencedAssemblies.Any(r =>
            r.Equals("Microsoft.Extensions.Hosting", StringComparison.Ordinal)) ?? false;

        var flavor = (aspNet, grpc, blazorServer, workerLike) switch
        {
            (true, _, true, _) => AppFlavor.DotNetBlazorServer,
            (_, true, _, _) => AppFlavor.DotNetGrpc,
            (true, _, _, _) => AppFlavor.DotNetAspNetCore,
            (_, _, _, true) => AppFlavor.DotNetWorker,
            _ => AppFlavor.DotNetConsole
        };

        var (os, arch) = TfmParser.ParseRid(depsModel.RuntimeIdentifier);
        var publishMode = DeterminePublishMode(layout, depsModel, runtimeConfig);

        var primaryFramework = frameworks.Count > 0 ? frameworks[0] : null;
        var tfm = TfmParser.Resolve(runtimeConfig?.Tfm, primaryFramework?.Version);

        var runtime = new RuntimeInfo
        {
            Name = ".NET",
            Version = primaryFramework?.Version,
            Tfm = tfm,
            RuntimeIdentifier = depsModel.RuntimeIdentifier,
            TargetOs = os,
            TargetArch = arch,
            PublishMode = publishMode,
            FrameworkReferences = [.. frameworks.Select(f => f.Name)]
        };

        var managed = depsModel.Libraries
            .Where(l => l.Type.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                        l.Type.Equals("project", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.Name, StringComparer.Ordinal)
            .Select(l => new ManagedDependency { Name = l.Name, Version = l.Version, Source = l.Type })
            .ToArray();

        var native = depsModel.NativeFiles
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new NativeDependency { Name = n })
            .ToArray();

        var requiresIcu = !(runtimeConfig?.InvariantGlobalization ?? false);
        var requiresGdiPlus = managed.Any(d =>
            d.Name.Equals("System.Drawing.Common", StringComparison.OrdinalIgnoreCase));

        var deps = new DependencyInfo
        {
            Managed = managed,
            Native = native,
            RequiresIcu = requiresIcu,
            RequiresTimezone = managed.Any(d => d.Name.Contains("TimeZone", StringComparison.OrdinalIgnoreCase)) || aspNet,
            RequiresGdiPlus = requiresGdiPlus
        };

        var ports = new HashSet<int>(hints.Ports);
        var listensHttp = hints.ListensHttp;
        var listensHttps = hints.ListensHttps;
        if (aspNet && ports.Count == 0)
        {
            ports.Add(8080);
            listensHttp = true;
            warnings.Add("no urls/ports found in appsettings; defaulting ASP.NET Core to 8080");
        }

        var network = new NetworkInfo
        {
            Ports = [.. ports.OrderBy(p => p)],
            Urls = hints.Urls,
            ListensHttp = listensHttp,
            ListensHttps = listensHttps,
            RequiresIngress = aspNet
        };

        EntryPoint? entry = null;
        if (assembly is not null && assembly.HasEntryPoint && layout.HasMainAssembly)
        {
            entry = new EntryPoint
            {
                Path = layout.MainAssemblyPath!,
                AssemblyName = assembly.AssemblyName,
                StartupCommand = "dotnet",
                Arguments = [Path.GetFileName(layout.MainAssemblyPath!)]
            };
        }
        else if (layout.AppHostPath is not null)
        {
            entry = new EntryPoint
            {
                Path = layout.AppHostPath,
                AssemblyName = assembly?.AssemblyName,
                StartupCommand = $"./{Path.GetFileName(layout.AppHostPath)}",
                Arguments = []
            };
        }

        if (publishMode == PublishMode.SelfContained || publishMode == PublishMode.NativeAot)
        {
            if (depsModel.RuntimeIdentifier is null)
            {
                warnings.Add("self-contained-like layout but RID missing in deps.json");
            }
        }

        var envHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["DOTNET_USE_POLLING_FILE_WATCHER"] = "false",
            ["DOTNET_EnableDiagnostics"] = "0"
        };
        if (aspNet)
        {
            envHints["ASPNETCORE_ENVIRONMENT"] = "Production";
            if (network.Ports.Count > 0)
            {
                envHints["ASPNETCORE_HTTP_PORTS"] = string.Join(';', network.Ports);
            }
        }
        if (runtimeConfig?.InvariantGlobalization == false)
        {
            envHints["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "false";
        }

        return new AppDescriptor
        {
            SourcePath = detection.SourcePath,
            Kind = AppKind.DotNet,
            Flavor = flavor,
            Runtime = runtime,
            Network = network,
            Dependencies = deps,
            EntryPoint = entry,
            EnvironmentHints = envHints,
            Warnings = warnings,
            DetectionConfidence = detection.Confidence
        };
    }

    private static AppDescriptor AnalyzeSourceOnly(DetectionResult detection)
    {
        return new AppDescriptor
        {
            SourcePath = detection.SourcePath,
            Kind = AppKind.DotNet,
            Flavor = detection.Flavor,
            Runtime = new RuntimeInfo { Name = ".NET" },
            Network = new NetworkInfo(),
            Dependencies = new DependencyInfo(),
            EntryPoint = null,
            EnvironmentHints = new Dictionary<string, string>(),
            Warnings = [.. detection.Warnings, "source-only analysis; publish required for full report"],
            DetectionConfidence = detection.Confidence
        };
    }

    private static PublishMode DeterminePublishMode(
        DotNetPublishLayout layout,
        DepsJsonModel deps,
        RuntimeConfig? runtimeConfig)
    {
        if (layout.AppHostPath is not null && deps.RuntimeIdentifier is not null)
        {
            if (!layout.HasMainAssembly)
            {
                return PublishMode.NativeAot;
            }
            return PublishMode.SelfContained;
        }

        if (deps.RuntimeIdentifier is not null)
        {
            return PublishMode.SelfContained;
        }

        return PublishMode.FrameworkDependent;
    }

    private static bool IsAspNetAssembly(string name)
    {
        return name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);
    }
}
