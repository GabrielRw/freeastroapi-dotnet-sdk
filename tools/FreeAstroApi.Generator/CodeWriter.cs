using System.Collections.Generic;
using System.Text;

namespace FreeAstroApi.Generator;

internal sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public CodeWriter(string header) => _sb.Append(header).Append('\n');

    public void Line(string text = "")
    {
        if (text.Length == 0) _sb.Append('\n');
        else _sb.Append(' ', _indent * 4).Append(text).Append('\n');
    }

    public void Open()
    {
        Line("{");
        _indent++;
    }

    public void Close() => CloseWith("}");

    public void CloseWith(string text)
    {
        TrimBlankLine();
        _indent--;
        Line(text);
    }

    /// <summary>Removes a trailing blank line, so blocks never end with one.</summary>
    public void TrimBlankLine()
    {
        if (_sb.Length >= 2 && _sb[^1] == '\n' && _sb[^2] == '\n') _sb.Length--;
    }

    public void Summary(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        Line("/// <summary>");
        foreach (var line in lines) Line(line.Length == 0 ? "///" : "/// " + line);
        Line("/// </summary>");
    }

    public override string ToString() => _sb.ToString();
}
