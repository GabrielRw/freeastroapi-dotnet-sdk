using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeAstroApi.Generator;

/// <summary>Builds XML documentation lines from schema descriptions, defaults and allowed values.</summary>
internal static class Docs
{
    private const int MaxEnumValues = 40;
    private const int MaxDefaultLength = 120;

    public static string Escape(string text) =>
        text.Replace("&", "&amp;", System.StringComparison.Ordinal)
            .Replace("<", "&lt;", System.StringComparison.Ordinal)
            .Replace(">", "&gt;", System.StringComparison.Ordinal);

    public static List<string> Lines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var lines = text.Replace("\r", "", System.StringComparison.Ordinal).Split('\n').Select(l => Escape(l.TrimEnd())).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        // Collapse runs of blank lines.
        return lines.Where((l, i) => l.Length > 0 || lines[i - 1].Length > 0).ToList();
    }

    public static List<string> ForSchema(JsonNode? node, JsonObject schemas)
    {
        if (node is not JsonObject s) return [];
        var lines = Lines(s["description"]?.GetValue<string>());
        AppendFacts(s, lines);
        return lines;
    }

    public static List<string> ForParameter(JsonObject parameter, JsonObject schemas)
    {
        var schema = parameter["schema"] as JsonObject;
        var lines = Lines(parameter["description"]?.GetValue<string>() ?? schema?["description"]?.GetValue<string>());
        if (parameter["deprecated"]?.GetValueKind() == JsonValueKind.True) lines.Add("Deprecated.");
        if (schema is not null) AppendFacts(schema, lines);
        return lines;
    }

    private static void AppendFacts(JsonObject s, List<string> lines)
    {
        var facts = new List<string>();
        if (s["deprecated"]?.GetValueKind() == JsonValueKind.True) facts.Add("Deprecated.");

        var enumValues = EnumValues(s);
        if (enumValues.Count > 0)
        {
            var shown = enumValues.Take(MaxEnumValues).Select(v => $"<c>{Escape(v)}</c>");
            facts.Add("Allowed values: " + string.Join(", ", shown) + (enumValues.Count > MaxEnumValues ? ", ..." : "") + ".");
        }
        if (s["const"] is { } constant) facts.Add($"Always <c>{Escape(Render(constant))}</c>.");
        if (s.ContainsKey("default"))
        {
            var rendered = Render(s["default"]);
            if (rendered.Length <= MaxDefaultLength) facts.Add($"Default: <c>{Escape(rendered)}</c>.");
        }
        if (facts.Count == 0) return;
        if (lines.Count > 0) lines.Add("");
        lines.Add(string.Join(" ", facts));
    }

    private static List<string> EnumValues(JsonObject s)
    {
        if (s["enum"] is JsonArray direct) return direct.Where(v => v is not null).Select(v => Render(v)).ToList();
        if ((s["anyOf"] ?? s["oneOf"]) is JsonArray union)
        {
            return union.OfType<JsonObject>().SelectMany(m => m["enum"] is JsonArray e ? e.Where(v => v is not null).Select(v => Render(v)) : [])
                .Distinct().ToList();
        }
        return [];
    }

    private static string Render(JsonNode? value) => value switch
    {
        null => "null",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => value.ToJsonString(),
    };
}
