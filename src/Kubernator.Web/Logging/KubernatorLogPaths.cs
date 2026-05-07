namespace Kubernator.Web.Logging;

internal static class KubernatorLogPaths
{
    public static string ResolveLogDirectory()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        var dir = Path.Combine(home, "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ResolveWebLogPath() => Path.Combine(ResolveLogDirectory(), "kubernator.web-.log");
}
