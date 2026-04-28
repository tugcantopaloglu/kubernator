using System.Formats.Tar;

namespace Kubernator.Runtime.Docker;

internal static class TarStream
{
    public static async Task CreateAsync(string sourceDirectory, string outputTarPath, CancellationToken ct)
    {
        await using var fileStream = File.Create(outputTarPath);
        await TarFile.CreateFromDirectoryAsync(sourceDirectory, fileStream, includeBaseDirectory: false, ct);
    }
}
