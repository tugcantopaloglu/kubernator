using Kubernator.Core.Abstractions;
using Kubernator.Core.Models;

namespace Kubernator.Core.Detection.Python;

public sealed class PythonDetector : IAppDetector
{
    public AppKind Handles => AppKind.Python;

    public Task<DetectionResult> DetectAsync(string path, CancellationToken ct = default)
    {
        var resolved = Path.GetFullPath(path);
        if (File.Exists(resolved))
        {
            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir))
            {
                resolved = dir;
            }
        }
        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var signals = new List<string>();
        var warnings = new List<string>();
        var flavor = AppFlavor.PythonGeneric;
        var confidence = 0.0;

        var requirements = Path.Combine(resolved, "requirements.txt");
        var pyproject = Path.Combine(resolved, "pyproject.toml");
        var pipfile = Path.Combine(resolved, "Pipfile");
        var setupPy = Path.Combine(resolved, "setup.py");

        if (File.Exists(requirements))
        {
            signals.Add("requirements.txt");
            confidence = 0.8;
        }
        if (File.Exists(pyproject))
        {
            signals.Add("pyproject.toml");
            confidence = Math.Max(confidence, 0.85);
        }
        if (File.Exists(pipfile))
        {
            signals.Add("Pipfile");
            confidence = Math.Max(confidence, 0.8);
        }
        if (File.Exists(setupPy))
        {
            signals.Add("setup.py");
            confidence = Math.Max(confidence, 0.7);
        }

        if (Directory.EnumerateFiles(resolved, "*.py", SearchOption.TopDirectoryOnly).Any())
        {
            signals.Add(".py source files at root");
            confidence = Math.Max(confidence, 0.6);
        }

        if (confidence == 0)
        {
            return Task.FromResult(DetectionResult.None(resolved));
        }

        var deps = ReadAllRequirements(resolved);
        if (deps.Contains("fastapi", StringComparer.OrdinalIgnoreCase))
        {
            flavor = AppFlavor.PythonFastApi;
            signals.Add("framework: fastapi");
        }
        else if (deps.Contains("flask", StringComparer.OrdinalIgnoreCase))
        {
            flavor = AppFlavor.PythonFlask;
            signals.Add("framework: flask");
        }
        else if (deps.Contains("django", StringComparer.OrdinalIgnoreCase) ||
                 File.Exists(Path.Combine(resolved, "manage.py")))
        {
            flavor = AppFlavor.PythonDjango;
            signals.Add("framework: django");
        }

        if (!Directory.Exists(Path.Combine(resolved, "site-packages")) &&
            !Directory.Exists(Path.Combine(resolved, ".venv", "lib")) &&
            !Directory.Exists(Path.Combine(resolved, "venv", "lib")))
        {
            warnings.Add("no installed dependencies detected; vendor packages before bundling for offline use");
        }

        return Task.FromResult(new DetectionResult
        {
            SourcePath = resolved,
            Kind = AppKind.Python,
            Flavor = flavor,
            Confidence = confidence,
            Signals = signals,
            Warnings = warnings
        });
    }

    private static HashSet<string> ReadAllRequirements(string root)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requirements = Path.Combine(root, "requirements.txt");
        if (File.Exists(requirements))
        {
            foreach (var line in File.ReadAllLines(requirements))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }
                var name = ExtractPackageName(trimmed);
                if (!string.IsNullOrEmpty(name))
                {
                    deps.Add(name);
                }
            }
        }
        var pyproject = Path.Combine(root, "pyproject.toml");
        if (File.Exists(pyproject))
        {
            foreach (var line in File.ReadAllLines(pyproject))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('"') || trimmed.StartsWith('\''))
                {
                    var stripped = trimmed.Trim('"', '\'', ',', ' ');
                    var name = ExtractPackageName(stripped);
                    if (!string.IsNullOrEmpty(name))
                    {
                        deps.Add(name);
                    }
                }
            }
        }
        return deps;
    }

    private static string ExtractPackageName(string raw)
    {
        var stops = new[] { '=', '<', '>', '!', '~', ';', '[', ' ' };
        var idx = raw.IndexOfAny(stops);
        var name = idx < 0 ? raw : raw[..idx];
        return name.Trim();
    }
}
