namespace Kubernator.Core.Generation.Emitters;

internal static class DockerignoreEmitter
{
    private static readonly string[] Patterns =
    [
        "**/bin",
        "**/obj",
        "**/.git",
        "**/.gitignore",
        "**/.gitattributes",
        "**/.vs",
        "**/.vscode",
        "**/.idea",
        "**/node_modules",
        "**/*.pdb",
        "**/*.log",
        "**/TestResults",
        ".kubernator/",
        "*.kubpack"
    ];

    public static string Emit()
    {
        var w = new IndentedTextWriter();
        foreach (var p in Patterns)
        {
            w.Line(p);
        }
        return w.ToString();
    }
}
