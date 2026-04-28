using Kubernator.Core.Models;

namespace Kubernator.Core.Strategy;

internal static class BaseImageCatalog
{
    public static BaseImage SelectFor(AppDescriptor app)
    {
        return app.Kind switch
        {
            AppKind.DotNet => SelectForDotNet(app),
            _ => throw new NotSupportedException($"No base image strategy for {app.Kind}")
        };
    }

    private static BaseImage SelectForDotNet(AppDescriptor app)
    {
        var versionTag = NormalizeTag(app.Runtime.Version, app.Runtime.Tfm);
        var requiresExtras = app.Dependencies.RequiresIcu || app.Dependencies.RequiresTimezone;

        var aspNet = app.Flavor is AppFlavor.DotNetAspNetCore
            or AppFlavor.DotNetBlazorServer
            or AppFlavor.DotNetGrpc;

        if (aspNet)
        {
            var tag = requiresExtras
                ? $"{versionTag}-noble-chiseled-extra"
                : $"{versionTag}-noble-chiseled";
            return new BaseImage
            {
                Registry = AllowedRegistries.Microsoft,
                Repository = "dotnet/aspnet",
                Tag = tag,
                DisplayName = $".NET {versionTag} ASP.NET (chiseled)",
                NonRootByDefault = true,
                RootlessSupportsExec = false,
                HasShell = false,
                DefaultUserId = 1654,
                DefaultGroupId = 1654,
                Notes = "Microsoft chiseled Ubuntu image; non-root UID 1654; no shell, no package manager."
            };
        }

        if (app.Runtime.PublishMode is PublishMode.NativeAot or PublishMode.SelfContained)
        {
            return new BaseImage
            {
                Registry = AllowedRegistries.Chainguard,
                Repository = "chainguard/static",
                Tag = "latest-glibc",
                DisplayName = "Chainguard static (glibc)",
                NonRootByDefault = true,
                RootlessSupportsExec = false,
                HasShell = false,
                DefaultUserId = 65532,
                DefaultGroupId = 65532,
                Notes = "Minimal scratch-like image with ca-certificates and tzdata; suitable for self-contained or AOT publish."
            };
        }

        var runtimeTag = requiresExtras
            ? $"{versionTag}-noble-chiseled-extra"
            : $"{versionTag}-noble-chiseled";
        return new BaseImage
        {
            Registry = AllowedRegistries.Microsoft,
            Repository = "dotnet/runtime",
            Tag = runtimeTag,
            DisplayName = $".NET {versionTag} runtime (chiseled)",
            NonRootByDefault = true,
            RootlessSupportsExec = false,
            HasShell = false,
            DefaultUserId = 1654,
            DefaultGroupId = 1654,
            Notes = "Microsoft chiseled Ubuntu runtime image; non-root UID 1654; no shell, no package manager."
        };
    }

    private static string NormalizeTag(string? version, string? tfm)
    {
        if (!string.IsNullOrEmpty(version))
        {
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                return $"{parts[0]}.{parts[1]}";
            }
        }

        if (!string.IsNullOrEmpty(tfm) && tfm.StartsWith("net", StringComparison.Ordinal))
        {
            return tfm[3..];
        }

        return "10.0";
    }
}
