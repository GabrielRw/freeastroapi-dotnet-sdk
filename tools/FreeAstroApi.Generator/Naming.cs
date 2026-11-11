using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeAstroApi.Generator;

internal static partial class Naming
{
    private static readonly HashSet<string> Keywords = new(System.StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex Separators();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    public static IEnumerable<string> Words(string text) =>
        Separators().Split(text).Where(w => w.Length > 0);

    /// <summary>snake_case / kebab-case / camelCase to PascalCase, keeping inner capitals.</summary>
    public static string Pascal(string text)
    {
        var sb = new StringBuilder();
        foreach (var word in Words(text)) sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
        var result = sb.ToString();
        if (result.Length == 0) return "Value";
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    public static string Camel(string text)
    {
        var pascal = Pascal(text);
        if (pascal.StartsWith('_')) return pascal;
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return Keywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>Schema names are kept verbatim when they are valid identifiers (parity with the other SDKs).</summary>
    public static string TypeName(string schemaName) =>
        Identifier().IsMatch(schemaName) && !Keywords.Contains(schemaName) ? schemaName : Pascal(schemaName);

    public static string Unique(string name, ISet<string> taken)
    {
        var candidate = name;
        for (var i = 2; taken.Contains(candidate); i++) candidate = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        taken.Add(candidate);
        return candidate;
    }
}
