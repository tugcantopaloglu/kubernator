using System.IO.Compression;
using System.Text;

namespace Kubernator.Core.Detection.Java;

internal static class JarReader
{
    public static JarMetadata Read(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");

        var manifest = manifestEntry is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ReadManifest(manifestEntry);

        var isSpringBoot = manifest.ContainsKey("Spring-Boot-Version")
            || archive.Entries.Any(e => e.FullName.StartsWith("BOOT-INF/", StringComparison.Ordinal));

        var isQuarkus = archive.Entries.Any(e =>
            e.FullName.StartsWith("META-INF/quarkus/", StringComparison.Ordinal) ||
            e.FullName.Equals("io/quarkus/runtime/Quarkus.class", StringComparison.Ordinal));

        var isWar = jarPath.EndsWith(".war", StringComparison.OrdinalIgnoreCase)
            || archive.Entries.Any(e => e.FullName.StartsWith("WEB-INF/", StringComparison.Ordinal));

        var props = ReadApplicationProperties(archive);

        manifest.TryGetValue("Main-Class", out var mainClass);
        manifest.TryGetValue("Start-Class", out var startClass);
        manifest.TryGetValue("Implementation-Version", out var version);
        manifest.TryGetValue("Implementation-Title", out var title);

        return new JarMetadata
        {
            JarPath = jarPath,
            MainClass = mainClass,
            StartClass = startClass,
            ImplementationVersion = version,
            ImplementationTitle = title,
            IsSpringBoot = isSpringBoot,
            IsQuarkus = isQuarkus,
            IsWar = isWar,
            ApplicationProperties = props
        };
    }

    private static Dictionary<string, string> ReadManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        string? currentKey = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith(' '))
            {
                current.Append(line.AsSpan(1));
                continue;
            }
            if (currentKey is not null)
            {
                dict[currentKey] = current.ToString();
                current.Clear();
                currentKey = null;
            }
            if (line.Length == 0)
            {
                continue;
            }
            var sep = line.IndexOf(':', StringComparison.Ordinal);
            if (sep <= 0)
            {
                continue;
            }
            currentKey = line[..sep].Trim();
            current.Append(line.AsSpan(sep + 1).TrimStart());
        }
        if (currentKey is not null)
        {
            dict[currentKey] = current.ToString();
        }
        return dict;
    }

    private static Dictionary<string, string> ReadApplicationProperties(ZipArchive archive)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "BOOT-INF/classes/application.properties", "application.properties", "WEB-INF/classes/application.properties" })
        {
            var entry = archive.GetEntry(name);
            if (entry is null)
            {
                continue;
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '!')
                {
                    continue;
                }
                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    continue;
                }
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                dict[key] = value;
            }
        }
        return dict;
    }
}
