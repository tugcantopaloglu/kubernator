using System.Text;

namespace Kubernator.Core.Generation.Emitters;

internal sealed class IndentedTextWriter
{
    private readonly StringBuilder buffer = new();
    private int indent;

    public void Indent() => indent++;
    public void Outdent() => indent = Math.Max(0, indent - 1);

    public void Line(string text)
    {
        if (text.Length == 0)
        {
            buffer.Append('\n');
            return;
        }
        for (int i = 0; i < indent; i++)
        {
            buffer.Append("  ");
        }
        buffer.Append(text);
        buffer.Append('\n');
    }

    public void Blank() => buffer.Append('\n');

    public void Raw(string block)
    {
        var lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                buffer.Append('\n');
                continue;
            }
            for (int i = 0; i < indent; i++)
            {
                buffer.Append("  ");
            }
            buffer.Append(line);
            buffer.Append('\n');
        }
    }

    public override string ToString() => buffer.ToString();
}
