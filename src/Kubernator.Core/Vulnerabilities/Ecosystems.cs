using Kubernator.Core.Models;

namespace Kubernator.Core.Vulnerabilities;

public static class Ecosystems
{
    public const string NuGet = "NuGet";
    public const string Npm = "npm";
    public const string PyPI = "PyPI";
    public const string Maven = "Maven";
    public const string Go = "Go";

    public static string? FromAppKind(AppKind kind) => kind switch
    {
        AppKind.DotNet => NuGet,
        AppKind.NodeJs => Npm,
        AppKind.StaticWeb => Npm,
        AppKind.Python => PyPI,
        AppKind.Java => Maven,
        AppKind.Go => Go,
        _ => null
    };

    public static string Normalize(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "nuget" => NuGet,
            "npm" => Npm,
            "pypi" or "python" => PyPI,
            "maven" or "java" => Maven,
            "go" or "golang" => Go,
            _ => raw
        };
    }

    public static string OsvDownloadUrl(string ecosystem) =>
        $"https://osv-vulnerabilities.storage.googleapis.com/{ecosystem}/all.zip";
}
