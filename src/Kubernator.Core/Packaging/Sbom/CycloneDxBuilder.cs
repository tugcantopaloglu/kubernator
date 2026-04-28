using System.Text.Encodings.Web;
using System.Text.Json;
using Kubernator.Core.Models;

namespace Kubernator.Core.Packaging.Sbom;

internal static class CycloneDxBuilder
{
    public static string Build(AppDescriptor app, string toolVersion)
    {
        var serialNumber = $"urn:uuid:{Guid.NewGuid():D}";
        var components = app.Dependencies.Managed
            .Select(m => new
            {
                type = "library",
                name = m.Name,
                version = m.Version,
                purl = $"pkg:nuget/{Uri.EscapeDataString(m.Name)}@{Uri.EscapeDataString(m.Version)}"
            })
            .Cast<object>()
            .Concat(app.Dependencies.Native.Select(n => new
            {
                type = "library",
                name = n.Name,
                version = "0",
                purl = $"pkg:generic/{Uri.EscapeDataString(n.Name)}"
            }))
            .ToArray();

        var doc = new
        {
            bomFormat = "CycloneDX",
            specVersion = "1.5",
            serialNumber,
            version = 1,
            metadata = new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                tools = new[]
                {
                    new { vendor = "kubernator", name = "kubernator", version = toolVersion }
                },
                component = new
                {
                    type = "application",
                    name = app.EntryPoint?.AssemblyName ?? "app",
                    version = app.Runtime.Version ?? "0.0.0"
                }
            },
            components
        };

        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
