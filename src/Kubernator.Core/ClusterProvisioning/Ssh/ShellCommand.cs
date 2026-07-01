namespace Kubernator.Core.ClusterProvisioning.Ssh;

internal static class ShellCommand
{
    public static string WrapSudo(string commandLine, string? sudoPassword)
    {
        var inner = Quote(commandLine);
        return string.IsNullOrEmpty(sudoPassword)
            ? $"sudo -n bash -c {inner}"
            : $"echo {Quote(sudoPassword)} | sudo -S -p '' bash -c {inner}";
    }

    public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public static string ToOctal(UnixFileMode mode) => Convert.ToString((int)mode, 8).PadLeft(3, '0');
}

internal static class PosixPath
{
    public static string GetDirectory(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    public static string GetFileName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx < 0 ? path : path[(idx + 1)..];
    }
}
