using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Analysis.Python;

public sealed class PythonAnalyzer : IAppAnalyzer
{
    public AppKind Handles => AppKind.Python;

    public Task<AppDescriptor> AnalyzeAsync(DetectionResult detection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = detection.SourcePath;
        var warnings = new List<string>(detection.Warnings);

        var managed = ReadRequirements(path);

        var (port, listensHttp, requiresIngress) = ResolveNetwork(detection.Flavor);
        var entry = ResolveEntry(detection.Flavor, path, warnings);

        var runtime = new RuntimeInfo
        {
            Name = "Python",
            TargetOs = TargetOs.Linux,
            TargetArch = TargetArchitecture.Unknown,
            PublishMode = PublishMode.FrameworkDependent
        };

        var network = new NetworkInfo
        {
            Ports = listensHttp ? [port] : [],
            ListensHttp = listensHttp,
            RequiresIngress = requiresIngress
        };

        var deps = new DependencyInfo
        {
            Managed = managed,
            Native = [],
            RequiresIcu = false,
            RequiresTimezone = true
        };

        var envHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PYTHONUNBUFFERED"] = "1",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            ["PIP_NO_CACHE_DIR"] = "1"
        };
        if (listensHttp)
        {
            envHints["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Task.FromResult(new AppDescriptor
        {
            SourcePath = path,
            Kind = AppKind.Python,
            Flavor = detection.Flavor,
            Runtime = runtime,
            Network = network,
            Dependencies = deps,
            EntryPoint = entry,
            EnvironmentHints = envHints,
            Warnings = warnings,
            DetectionConfidence = detection.Confidence
        });
    }

    private static (int Port, bool ListensHttp, bool RequiresIngress) ResolveNetwork(AppFlavor flavor)
    {
        return flavor switch
        {
            AppFlavor.PythonFastApi => (8000, true, true),
            AppFlavor.PythonFlask => (5000, true, true),
            AppFlavor.PythonDjango => (8000, true, true),
            _ => (8080, false, false)
        };
    }

    private static EntryPoint ResolveEntry(AppFlavor flavor, string root, List<string> warnings)
    {
        if (flavor == AppFlavor.PythonFastApi)
        {
            var module = ProbeModule(root, ["main", "app", "asgi"]);
            return new EntryPoint
            {
                Path = root,
                AssemblyName = module,
                StartupCommand = "uvicorn",
                Arguments = [$"{module}:app", "--host", "0.0.0.0", "--port", "8000"]
            };
        }
        if (flavor == AppFlavor.PythonDjango)
        {
            var settings = ProbeDjangoSettings(root);
            return new EntryPoint
            {
                Path = root,
                AssemblyName = settings,
                StartupCommand = "gunicorn",
                Arguments = [$"{settings}.wsgi:application", "--bind", "0.0.0.0:8000"]
            };
        }
        if (flavor == AppFlavor.PythonFlask)
        {
            var module = ProbeModule(root, ["app", "main", "wsgi"]);
            return new EntryPoint
            {
                Path = root,
                AssemblyName = module,
                StartupCommand = "gunicorn",
                Arguments = [$"{module}:app", "--bind", "0.0.0.0:5000"]
            };
        }

        var fallback = ProbeModule(root, ["main", "app"]);
        if (!File.Exists(Path.Combine(root, $"{fallback}.py")))
        {
            warnings.Add($"could not resolve a python entry module; defaulting to `python {fallback}.py`");
        }
        return new EntryPoint
        {
            Path = Path.Combine(root, $"{fallback}.py"),
            AssemblyName = fallback,
            StartupCommand = "python",
            Arguments = [$"{fallback}.py"]
        };
    }

    private static string ProbeModule(string root, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (File.Exists(Path.Combine(root, $"{name}.py")))
            {
                return name;
            }
        }
        return candidates[0];
    }

    private static string ProbeDjangoSettings(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (File.Exists(Path.Combine(dir, "wsgi.py")) || File.Exists(Path.Combine(dir, "settings.py")))
            {
                return Path.GetFileName(dir);
            }
        }
        return "project";
    }

    private static IReadOnlyList<ManagedDependency> ReadRequirements(string root)
    {
        var requirements = Path.Combine(root, "requirements.txt");
        if (!File.Exists(requirements))
        {
            return [];
        }
        var deps = new List<ManagedDependency>();
        foreach (var line in File.ReadAllLines(requirements))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }
            var stops = new[] { '=', '<', '>', '!', '~', ';', '[', ' ' };
            var idx = trimmed.IndexOfAny(stops);
            var name = idx < 0 ? trimmed : trimmed[..idx];
            var version = idx < 0 ? "*" : trimmed[idx..].TrimStart('=', '<', '>', '!', '~', ' ').Trim();
            deps.Add(new ManagedDependency { Name = name.Trim(), Version = version, Source = "pypi" });
        }
        return deps.OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
    }
}
