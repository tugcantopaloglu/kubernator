namespace Kubernator.Core.Detection.DotNet;

internal static class DotNetLayoutScanner
{
    public static IReadOnlyList<DotNetPublishLayout> Scan(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var depsFiles = Directory.EnumerateFiles(root, "*.deps.json", SearchOption.TopDirectoryOnly).ToArray();
        if (depsFiles.Length == 0)
        {
            return [];
        }

        var layouts = new List<DotNetPublishLayout>();
        foreach (var depsPath in depsFiles)
        {
            var fileName = Path.GetFileName(depsPath);
            var baseName = fileName[..^".deps.json".Length];

            var runtimeConfigPath = Path.Combine(root, baseName + ".runtimeconfig.json");
            var mainAssembly = Path.Combine(root, baseName + ".dll");
            var appHostExe = Path.Combine(root, baseName + ".exe");
            var appHostNoExt = Path.Combine(root, baseName);

            string? appHost = null;
            if (File.Exists(appHostExe))
            {
                appHost = appHostExe;
            }
            else if (File.Exists(appHostNoExt) && !OperatingSystem.IsWindows())
            {
                appHost = appHostNoExt;
            }

            layouts.Add(new DotNetPublishLayout
            {
                RootPath = root,
                AssemblyBaseName = baseName,
                DepsJsonPath = depsPath,
                RuntimeConfigPath = File.Exists(runtimeConfigPath) ? runtimeConfigPath : null,
                MainAssemblyPath = File.Exists(mainAssembly) ? mainAssembly : null,
                AppHostPath = appHost
            });
        }

        return layouts;
    }
}
