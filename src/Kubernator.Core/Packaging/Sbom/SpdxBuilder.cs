using System.Text.Encodings.Web;
using System.Text.Json;
using Kubernator.Core.Models;

namespace Kubernator.Core.Packaging.Sbom;

internal static class SpdxBuilder
{
    public static string Build(AppDescriptor app, string toolVersion)
    {
        var docNamespace = $"https://kubernator.dev/spdx/{Guid.NewGuid():D}";
        var rootPackage = SafeId(app.EntryPoint?.AssemblyName ?? "app");

        var packages = new List<object>
        {
            new
            {
                SPDXID = $"SPDXRef-Package-{rootPackage}",
                name = app.EntryPoint?.AssemblyName ?? "app",
                versionInfo = app.Runtime.Version ?? "0.0.0",
                downloadLocation = "NOASSERTION",
                filesAnalyzed = false,
                supplier = "NOASSERTION",
                licenseConcluded = "NOASSERTION",
                licenseDeclared = "NOASSERTION"
            }
        };

        var relationships = new List<object>
        {
            new
            {
                spdxElementId = "SPDXRef-DOCUMENT",
                relationshipType = "DESCRIBES",
                relatedSpdxElement = $"SPDXRef-Package-{rootPackage}"
            }
        };

        foreach (var m in app.Dependencies.Managed)
        {
            var id = $"SPDXRef-Package-{SafeId(m.Name)}-{SafeId(m.Version)}";
            packages.Add(new
            {
                SPDXID = id,
                name = m.Name,
                versionInfo = m.Version,
                downloadLocation = "NOASSERTION",
                filesAnalyzed = false,
                supplier = "NOASSERTION",
                licenseConcluded = "NOASSERTION",
                licenseDeclared = "NOASSERTION",
                externalRefs = new[]
                {
                    new
                    {
                        referenceCategory = "PACKAGE-MANAGER",
                        referenceType = "purl",
                        referenceLocator = $"pkg:nuget/{Uri.EscapeDataString(m.Name)}@{Uri.EscapeDataString(m.Version)}"
                    }
                }
            });
            relationships.Add(new
            {
                spdxElementId = $"SPDXRef-Package-{rootPackage}",
                relationshipType = "DEPENDS_ON",
                relatedSpdxElement = id
            });
        }

        foreach (var n in app.Dependencies.Native)
        {
            var id = $"SPDXRef-Package-native-{SafeId(n.Name)}";
            packages.Add(new
            {
                SPDXID = id,
                name = n.Name,
                versionInfo = "NOASSERTION",
                downloadLocation = "NOASSERTION",
                filesAnalyzed = false,
                supplier = "NOASSERTION",
                licenseConcluded = "NOASSERTION",
                licenseDeclared = "NOASSERTION"
            });
            relationships.Add(new
            {
                spdxElementId = $"SPDXRef-Package-{rootPackage}",
                relationshipType = "DEPENDS_ON",
                relatedSpdxElement = id
            });
        }

        var doc = new
        {
            spdxVersion = "SPDX-2.3",
            dataLicense = "CC0-1.0",
            SPDXID = "SPDXRef-DOCUMENT",
            name = $"sbom-{app.EntryPoint?.AssemblyName ?? "app"}",
            documentNamespace = docNamespace,
            creationInfo = new
            {
                created = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                creators = new[] { $"Tool: kubernator-{toolVersion}" }
            },
            packages,
            relationships
        };

        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private static string SafeId(string raw)
    {
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
