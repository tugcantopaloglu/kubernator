namespace Kubernator.Core.Tests.Fixtures;

internal sealed class TempPublishOutput : IDisposable
{
    public string Path { get; }

    private TempPublishOutput(string path)
    {
        Path = path;
    }

    public static TempPublishOutput Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kubernator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new TempPublishOutput(dir);
    }

    public string WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(full, content);
        return full;
    }

    public string CopyFile(string sourceFile, string targetRelativePath)
    {
        var full = System.IO.Path.Combine(Path, targetRelativePath);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.Copy(sourceFile, full, overwrite: true);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
