namespace Kubernator.Core.Analysis.DotNet;

internal static class TfmParser
{
    public static string? Resolve(string? tfm, string? frameworkVersion)
    {
        if (!string.IsNullOrEmpty(tfm))
        {
            return tfm;
        }
        if (string.IsNullOrEmpty(frameworkVersion))
        {
            return null;
        }
        var parts = frameworkVersion.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }
        return $"net{parts[0]}.{parts[1]}";
    }

    public static (Models.TargetOs Os, Models.TargetArchitecture Arch) ParseRid(string? rid)
    {
        if (string.IsNullOrEmpty(rid))
        {
            return (Models.TargetOs.Unknown, Models.TargetArchitecture.Unknown);
        }

        var os = Models.TargetOs.Unknown;
        var arch = Models.TargetArchitecture.Unknown;

        var lower = rid.ToLowerInvariant();
        if (lower.StartsWith("linux-musl", StringComparison.Ordinal))
        {
            os = Models.TargetOs.LinuxMusl;
        }
        else if (lower.StartsWith("linux", StringComparison.Ordinal))
        {
            os = Models.TargetOs.Linux;
        }
        else if (lower.StartsWith("win", StringComparison.Ordinal))
        {
            os = Models.TargetOs.Windows;
        }
        else if (lower.StartsWith("osx", StringComparison.Ordinal) || lower.StartsWith("mac", StringComparison.Ordinal))
        {
            os = Models.TargetOs.Osx;
        }

        if (lower.EndsWith("-x64", StringComparison.Ordinal))
        {
            arch = Models.TargetArchitecture.X64;
        }
        else if (lower.EndsWith("-arm64", StringComparison.Ordinal))
        {
            arch = Models.TargetArchitecture.Arm64;
        }
        else if (lower.EndsWith("-x86", StringComparison.Ordinal))
        {
            arch = Models.TargetArchitecture.X86;
        }
        else if (lower.EndsWith("-arm", StringComparison.Ordinal))
        {
            arch = Models.TargetArchitecture.Arm;
        }

        return (os, arch);
    }
}
