using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeAstroApi.Generator;

public sealed record GenerationResult(IReadOnlyDictionary<string, string> Files, int OperationCount, int ModelCount);

/// <summary>
/// Turns the FreeAstroAPI OpenAPI 3.1 document into C# models, query-parameter classes, resource
/// classes, an operation registry and a System.Text.Json source-generation context.
/// </summary>
public static class SdkGenerator
{
    public static GenerationResult Generate(string specJson) => new GeneratorRun(specJson).Run();
}

/// <summary>A C# type. <see cref="Decl"/> carries nullable annotations, <see cref="Runtime"/> is usable in typeof().</summary>
internal sealed record TypeRef(string Decl, string Runtime, bool IsValueType, bool IsCollection = false, string? ModelName = null)
{
    public static readonly TypeRef Object = new("object", "object", false);
    public static readonly TypeRef String = new("string", "string", false);
    public static readonly TypeRef Int = new("int", "int", true);
    public static readonly TypeRef Long = new("long", "long", true);
    public static readonly TypeRef Double = new("double", "double", true);
    public static readonly TypeRef Bool = new("bool", "bool", true);
    public static readonly TypeRef JsonElement = new("global::System.Text.Json.JsonElement", "global::System.Text.Json.JsonElement", true);

    public bool IsNumeric => this == Int || this == Long || this == Double;

    public string Annotated(bool nullable) => nullable ? Decl + "?" : Decl;
    public string RuntimeAnnotated(bool nullable) => nullable && IsValueType ? Runtime + "?" : Runtime;

    public static TypeRef ListOf(TypeRef item, bool itemNullable) => new(
        $"global::System.Collections.Generic.List<{item.Annotated(itemNullable)}>",
        $"global::System.Collections.Generic.List<{item.RuntimeAnnotated(itemNullable)}>",
        false,
        IsCollection: true);

    public static TypeRef MapOf(TypeRef value, bool valueNullable) => new(
        $"global::System.Collections.Generic.Dictionary<string, {value.Annotated(valueNullable)}>",
        $"global::System.Collections.Generic.Dictionary<string, {value.RuntimeAnnotated(valueNullable)}>",
        false,
        IsCollection: true);

    public static TypeRef Model(string name) => new($"global::FreeAstroApi.Models.{name}", $"global::FreeAstroApi.Models.{name}", false, ModelName: name);
}

internal readonly record struct Mapped(TypeRef Type, bool Nullable);

internal sealed class ModelDef(string name, JsonObject schema, string origin)
{
    public string Name { get; } = name;
    public JsonObject Schema { get; } = schema;
    public string Origin { get; } = origin;
    public List<PropertyDef> Properties { get; } = [];
}

internal sealed record PropertyDef(string JsonName, string CsName, TypeRef Type, bool Optional, IReadOnlyList<string> Doc);

internal enum ResponseKind { None, Json, Text, Content }

internal sealed class OperationDef
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required JsonObject Op { get; init; }
    public required int Version { get; init; }
    public required string Resource { get; init; }
    public required List<string> Rest { get; init; }
    public required List<JsonObject> PathParams { get; init; }
    public required List<JsonObject> QueryParams { get; init; }
    public JsonNode? BodySchema { get; init; }
    public bool HasBody { get; init; }
    public bool BodyRequired { get; init; }
    public string? Sub { get; set; }
    public string BaseName { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccessorPrefix => Naming.Pascal(Resource) + (Sub is null ? "" : Naming.Pascal(Sub));
    public string MethodName => Naming.Pascal(Name) + "Async";
    public string Accessor => string.Join(".", new[] { Naming.Pascal(Resource), Sub is null ? null : Naming.Pascal(Sub), MethodName }.Where(s => s is not null));
    public bool Deprecated => Op["deprecated"]?.GetValue<bool>() == true;
}

internal sealed class GeneratorRun
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];
    private static readonly HashSet<string> ConstraintKeys = new(StringComparer.Ordinal)
    {
        "required", "not", "title", "description", "minProperties", "maxProperties",
    };

    // Method names for operations whose path has nothing after the resource (kept in sync with the TS/Python SDKs).
    private static readonly Dictionary<string, string> EmptyName = new(StringComparer.Ordinal) { ["v1_sky_events"] = "list" };

    private readonly JsonObject _root;
    private readonly JsonObject _schemas;
    private readonly HashSet<string> _typeNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _componentClass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mapped> _aliasCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    private readonly Dictionary<JsonNode, ModelDef> _inline = new(ReferenceEqualityComparer.Instance);
    private readonly List<ModelDef> _models = [];
    private readonly List<ModelDef> _pending = [];
    private readonly HashSet<string> _rootTypes = new(StringComparer.Ordinal);

    public GeneratorRun(string specJson)
    {
        _root = JsonNode.Parse(specJson) as JsonObject ?? throw new GeneratorException("invalid OpenAPI document");
        if (_root["paths"] is not JsonObject) throw new GeneratorException("the OpenAPI document has no paths");
        _schemas = _root["components"]?["schemas"] as JsonObject ?? [];
        foreach (var reserved in new[] { "FreeAstroContent", "FreeAstroClient", "FreeAstroClientOptions", "RequestOptions" })
        {
            _typeNames.Add(reserved);
        }
    }

    public GenerationResult Run()
    {
        // 1. Component schemas that become classes get their names first, so $refs resolve.
        foreach (var (name, node) in _schemas)
        {
            if (node is JsonObject schema && IsClassLike(schema))
            {
                _componentClass[name] = Naming.Unique(Naming.TypeName(name), _typeNames);
            }
        }
        foreach (var (name, node) in _schemas)
        {
            if (_componentClass.TryGetValue(name, out var cls)) _pending.Add(new ModelDef(cls, (JsonObject)node!, name));
        }

        // 2. Operations (may discover inline request/response classes).
        var ops = AssignNames(CollectOperations());
        var paramsClasses = ops.Where(o => o.QueryParams.Count > 0)
            .ToDictionary(o => o, o => Naming.Unique(o.AccessorPrefix + Naming.Pascal(o.Name) + "Params", _typeNames));
        foreach (var name in paramsClasses.Values) _rootTypes.Add(TypeRef.Model(name).Runtime);
        var signatures = ops.ToDictionary(o => o, BuildSignature);

        // 3. Build every model (building one can discover more inline models).
        while (_pending.Count > 0)
        {
            var model = _pending[0];
            _pending.RemoveAt(0);
            BuildModel(model);
            _models.Add(model);
        }

        foreach (var model in _models) _rootTypes.Add(TypeRef.Model(model.Name).Runtime);

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Models.g.cs"] = RenderModels(),
            ["Parameters.g.cs"] = RenderParameters(ops, paramsClasses),
            ["Resources.g.cs"] = RenderResources(ops, paramsClasses, signatures),
            ["Operations.g.cs"] = RenderOperations(ops),
            ["FreeAstroJsonContext.g.cs"] = RenderJsonContext(),
        };
        return new GenerationResult(files, ops.Count, _models.Count);
    }

    // ------------------------------------------------------------------ schemas

    private static bool IsConstraintOnly(JsonNode? node) =>
        node is JsonObject o && o.All(p => ConstraintKeys.Contains(p.Key));

    private static bool HasProperties(JsonObject s) => s["properties"] is JsonObject p && p.Count > 0;

    private static List<JsonNode> AllOfMembers(JsonObject s) =>
        s["allOf"] is JsonArray a ? a.Where(m => m is not null && !IsConstraintOnly(m)).Select(m => m!).ToList() : [];

    private static bool IsClassLike(JsonObject s)
    {
        if (HasProperties(s)) return true;
        var allOf = AllOfMembers(s);
        return allOf.Count > 1;
    }

    private JsonObject? ResolveRef(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal)) throw new GeneratorException($"unsupported $ref '{reference}'");
        return _schemas[reference[prefix.Length..]] as JsonObject;
    }

    private static string RefName(string reference) => reference[(reference.LastIndexOf('/') + 1)..];

    private Mapped Map(JsonNode? node, string hint)
    {
        if (node is null) return new(TypeRef.Object, true);
        if (node is not JsonObject s) return new(TypeRef.Object, true); // boolean schemas
        if (s.Count == 0) return new(TypeRef.Object, true);

        var nullable = s["nullable"]?.GetValueKind() == JsonValueKind.True;

        if (s["$ref"]?.GetValue<string>() is { } reference)
        {
            var name = RefName(reference);
            if (_componentClass.TryGetValue(name, out var cls)) return new(TypeRef.Model(cls), nullable);
            if (_aliasCache.TryGetValue(name, out var cached)) return cached with { Nullable = cached.Nullable || nullable };
            var target = ResolveRef(reference) ?? throw new GeneratorException($"unresolved $ref '{reference}'");
            if (!_resolving.Add(name)) return new(TypeRef.Object, true); // recursive alias
            var mapped = Map(target, Naming.TypeName(name));
            _resolving.Remove(name);
            _aliasCache[name] = mapped;
            return mapped with { Nullable = mapped.Nullable || nullable };
        }

        var allOf = AllOfMembers(s);
        if (allOf.Count > 0 && !HasProperties(s))
        {
            if (allOf.Count == 1)
            {
                var inner = Map(allOf[0], hint);
                return inner with { Nullable = inner.Nullable || nullable };
            }
            return new(InlineModel(s, hint), nullable);
        }

        if (HasProperties(s)) return new(InlineModel(s, hint), nullable);

        if ((s["anyOf"] ?? s["oneOf"]) is JsonArray union)
        {
            var hasNull = union.Any(m => m is JsonObject mo && mo["type"]?.GetValueKind() == JsonValueKind.String && mo["type"]!.GetValue<string>() == "null");
            var members = union.Where(m => m is not null && !IsConstraintOnly(m) && !(m is JsonObject mo && mo["type"] is JsonValue t && t.GetValueKind() == JsonValueKind.String && t.GetValue<string>() == "null")).ToList();
            if (members.Count > 0)
            {
                var mapped = members.Select((m, i) => Map(m, members.Count == 1 ? hint : hint + "Option" + (i + 1).ToString(CultureInfo.InvariantCulture))).ToList();
                var anyNullable = nullable || hasNull || mapped.Any(m => m.Nullable);
                var distinct = mapped.Select(m => m.Type).Distinct().ToList();
                if (distinct.Count == 1) return new(distinct[0], anyNullable);
                if (distinct.All(t => t.IsNumeric)) return new(TypeRef.Double, anyNullable);
                return new(TypeRef.Object, true);
            }
            nullable |= hasNull;
            if (s["type"] is null && s["const"] is null && s["enum"] is null) return new(TypeRef.Object, true);
        }

        if (s["const"] is { } constant) return new(FromValueKind(constant.GetValueKind(), constant), nullable);

        var types = new List<string>();
        switch (s["type"])
        {
            case JsonValue v when v.GetValueKind() == JsonValueKind.String: types.Add(v.GetValue<string>()); break;
            case JsonArray a: types.AddRange(a.Select(t => t!.GetValue<string>())); break;
        }
        if (types.Remove("null")) nullable = true;

        if (types.Count == 0 && s["enum"] is JsonArray enumValues)
        {
            var kinds = enumValues.Where(e => e is not null).Select(e => FromValueKind(e!.GetValueKind(), e)).Distinct().ToList();
            if (enumValues.Any(e => e is null)) nullable = true;
            return kinds.Count == 1 ? new(kinds[0], nullable) : new(TypeRef.Object, true);
        }

        if (types.Count > 1)
        {
            var mapped = types.Select(t => MapType(t, s, hint).Type).Distinct().ToList();
            if (mapped.Count == 1) return new(mapped[0], nullable);
            return mapped.All(t => t.IsNumeric) ? new(TypeRef.Double, nullable) : new(TypeRef.Object, true);
        }

        if (types.Count == 1)
        {
            var mapped = MapType(types[0], s, hint);
            return mapped with { Nullable = mapped.Nullable || nullable };
        }

        if (s["items"] is not null) return new(MapType("array", s, hint).Type, nullable);
        if (s["additionalProperties"] is not null) return new(MapType("object", s, hint).Type, nullable);
        return new(TypeRef.Object, true);
    }

    private Mapped MapType(string type, JsonObject s, string hint)
    {
        switch (type)
        {
            case "string": return new(TypeRef.String, false);
            case "boolean": return new(TypeRef.Bool, false);
            case "number": return new(TypeRef.Double, false);
            case "integer": return new(IntegerType(s), false);
            case "null": return new(TypeRef.Object, true);
            case "array":
            {
                var item = Map(s["items"], hint + "Item");
                return new(TypeRef.ListOf(item.Type, item.Nullable), false);
            }
            case "object":
            {
                if (s["additionalProperties"] is JsonObject extra && extra.Count > 0)
                {
                    var value = Map(extra, hint + "Value");
                    return new(TypeRef.MapOf(value.Type, value.Nullable), false);
                }
                return new(TypeRef.MapOf(TypeRef.Object, true), false);
            }
            default: return new(TypeRef.Object, true);
        }
    }

    private static TypeRef IntegerType(JsonObject s)
    {
        if (s["format"]?.GetValue<string>() == "int64") return TypeRef.Long;
        static double? Number(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;
        var max = Number(s["maximum"]) ?? Number(s["exclusiveMaximum"]);
        var min = Number(s["minimum"]) ?? Number(s["exclusiveMinimum"]);
        if (max is > int.MaxValue || min is < int.MinValue) return TypeRef.Long;
        return TypeRef.Int;
    }

    private static TypeRef FromValueKind(JsonValueKind kind, JsonNode value) => kind switch
    {
        JsonValueKind.String => TypeRef.String,
        JsonValueKind.True or JsonValueKind.False => TypeRef.Bool,
        JsonValueKind.Number => value.ToJsonString().Contains('.', StringComparison.Ordinal) || value.ToJsonString().Contains('e', StringComparison.OrdinalIgnoreCase) ? TypeRef.Double : TypeRef.Int,
        _ => TypeRef.Object,
    };

    private TypeRef InlineModel(JsonObject schema, string hint)
    {
        if (!_inline.TryGetValue(schema, out var model))
        {
            var name = Naming.Unique(Naming.Pascal(hint), _typeNames);
            model = new ModelDef(name, schema, "inline schema");
            _inline[schema] = model;
            _pending.Add(model);
        }
        return TypeRef.Model(model.Name);
    }

    /// <summary>Own properties plus those merged from allOf members (resolving $refs).</summary>
    private void CollectProperties(JsonObject schema, List<KeyValuePair<string, JsonNode?>> props, HashSet<string> required, HashSet<string> visited)
    {
        foreach (var member in AllOfMembers(schema))
        {
            var target = member is JsonObject mo && mo["$ref"]?.GetValue<string>() is { } r ? ResolveRef(r) : member as JsonObject;
            if (target is null) continue;
            if (member is JsonObject m2 && m2["$ref"]?.GetValue<string>() is { } r2 && !visited.Add(r2)) continue;
            CollectProperties(target, props, required, visited);
        }
        if (schema["properties"] is JsonObject own)
        {
            foreach (var (key, value) in own)
            {
                var index = props.FindIndex(p => p.Key == key);
                if (index >= 0) props[index] = new(key, value);
                else props.Add(new(key, value));
            }
        }
        if (schema["required"] is JsonArray req)
        {
            foreach (var r in req) required.Add(r!.GetValue<string>());
        }
    }

    private void BuildModel(ModelDef model)
    {
        var props = new List<KeyValuePair<string, JsonNode?>>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        CollectProperties(model.Schema, props, required, new HashSet<string>(StringComparer.Ordinal));
        var taken = new HashSet<string>(StringComparer.Ordinal) { model.Name };
        foreach (var (jsonName, schema) in props)
        {
            var csName = Naming.Unique(Naming.Pascal(jsonName), taken);
            var mapped = Map(schema, model.Name + Naming.Pascal(jsonName));
            var optional = mapped.Nullable || !required.Contains(jsonName);
            var doc = Docs.ForSchema(schema, _schemas);
            if (doc.Count == 0) doc = [$"The <c>{Docs.Escape(jsonName)}</c> field."];
            model.Properties.Add(new PropertyDef(jsonName, csName, mapped.Type, optional, doc));
        }
    }

    // ------------------------------------------------------------------ operations

    private List<OperationDef> CollectOperations()
    {
        var ops = new List<OperationDef>();
        foreach (var (path, itemNode) in _root["paths"]!.AsObject())
        {
            if (itemNode is not JsonObject item) continue;
            var shared = item["parameters"] as JsonArray;
            foreach (var method in HttpMethods)
            {
                if (item[method] is not JsonObject op) continue;
                var id = op["operationId"]?.GetValue<string>() ?? throw new GeneratorException($"{method.ToUpperInvariant()} {path} has no operationId");

                int version;
                List<string> segments;
                var match = System.Text.RegularExpressions.Regex.Match(path, "^/api/v(\\d+)/(.*)$");
                if (path == "/natal")
                {
                    version = 1;
                    segments = ["natal", "simple"];
                }
                else if (match.Success)
                {
                    version = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    segments = match.Groups[2].Value.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                else
                {
                    throw new GeneratorException($"unexpected public path {path}; extend CollectOperations with a naming rule");
                }

                var parameters = (shared ?? []).Concat(op["parameters"] as JsonArray ?? [])
                    .Select(p => p is JsonObject po && po["$ref"]?.GetValue<string>() is { } pr ? ResolveParameter(pr) : p as JsonObject)
                    .Where(p => p is not null).Select(p => p!).ToList();

                JsonNode? body = null;
                var hasBody = false;
                if (op["requestBody"]?["content"] is JsonObject bodyContent)
                {
                    var jsonType = bodyContent.Select(p => p.Key).FirstOrDefault(IsJson);
                    if (jsonType is null)
                    {
                        throw new GeneratorException($"{id}: request content types [{string.Join(", ", bodyContent.Select(p => p.Key))}] are not supported; add support in the generator");
                    }
                    hasBody = true;
                    body = bodyContent[jsonType]?["schema"];
                }

                ops.Add(new OperationDef
                {
                    Id = id,
                    Method = method.ToUpperInvariant(),
                    Path = path,
                    Op = op,
                    Version = version,
                    Resource = Naming.Camel(segments[0]).TrimStart('@'),
                    Rest = segments.Skip(1).ToList(),
                    PathParams = parameters.Where(p => p["in"]?.GetValue<string>() == "path").ToList(),
                    QueryParams = parameters.Where(p => p["in"]?.GetValue<string>() == "query").ToList(),
                    BodySchema = body,
                    HasBody = hasBody,
                    BodyRequired = op["requestBody"]?["required"]?.GetValue<bool>() == true,
                });
            }
        }
        var duplicate = ops.GroupBy(o => o.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new GeneratorException($"duplicate operationId {duplicate.Key}");
        return ops;
    }

    private JsonObject? ResolveParameter(string reference)
    {
        const string prefix = "#/components/parameters/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal)) throw new GeneratorException($"unsupported parameter $ref '{reference}'");
        return _root["components"]?["parameters"]?[reference[prefix.Length..]] as JsonObject;
    }

    private static bool IsParam(string segment) => segment.StartsWith('{') && segment.EndsWith('}');

    /// <summary>Same naming rule as the TypeScript and Python SDKs.</summary>
    private static List<OperationDef> AssignNames(List<OperationDef> ops)
    {
        static string Camel(IEnumerable<string> parts)
        {
            var pascal = Naming.Pascal(string.Join("-", parts));
            return char.ToLowerInvariant(pascal[0]) + pascal[1..];
        }

        var direct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in ops)
        {
            var literalRest = o.Rest.Where(s => !IsParam(s)).ToList();
            if (literalRest.Count == 1) direct.Add($"{o.Resource}/{literalRest[0]}");
        }
        foreach (var o in ops)
        {
            var literalRest = o.Rest.Where(s => !IsParam(s)).ToList();
            var nameParts = literalRest;
            o.Sub = null;
            if (literalRest.Count >= 2 && !IsParam(o.Rest[0]) && !direct.Contains($"{o.Resource}/{o.Rest[0]}"))
            {
                o.Sub = Camel([o.Rest[0]]);
                nameParts = literalRest.Skip(1).ToList();
            }
            o.BaseName = nameParts.Count > 0 ? Camel(nameParts) : EmptyName.GetValueOrDefault(o.Id, "get");
        }
        foreach (var group in ops.GroupBy(o => $"{o.Resource}/{o.Sub}/{o.BaseName}"))
        {
            var latest = group.Max(o => o.Version);
            var seen = new Dictionary<int, OperationDef>();
            foreach (var o in group)
            {
                if (seen.TryGetValue(o.Version, out var other)) throw new GeneratorException($"name clash: {o.Id} and {other.Id}");
                seen[o.Version] = o;
                o.Name = o.Version == latest ? o.BaseName : $"{o.BaseName}V{o.Version}";
            }
        }
        return ops;
    }

    private static bool IsJson(string contentType) =>
        contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("/json", StringComparison.OrdinalIgnoreCase);

    private static bool IsText(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase);

    private sealed record Signature(ResponseKind Kind, TypeRef? Response, string Accept, TypeRef? Body, bool BodyNullable, List<string> ContentTypes);

    private Signature BuildSignature(OperationDef o)
    {
        TypeRef? body = null;
        var bodyNullable = false;
        if (o.HasBody)
        {
            var mapped = Map(o.BodySchema, o.AccessorPrefix + Naming.Pascal(o.Name) + "Request");
            body = mapped.Type;
            bodyNullable = mapped.Nullable;
            _rootTypes.Add(body.Runtime);
        }

        var responses = o.Op["responses"] as JsonObject ?? [];
        var successCode = responses.Select(p => p.Key).Where(k => k.Length == 3 && k[0] == '2').OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
            ?? (responses.ContainsKey("default") ? "default" : null);
        var content = successCode is null ? null : responses[successCode]?["content"] as JsonObject;
        var types = content?.Select(p => p.Key).ToList() ?? [];
        if (types.Count == 0) return new(ResponseKind.None, null, "application/json", body, bodyNullable, types);

        if (types.All(IsJson))
        {
            var schema = content![types[0]]?["schema"];
            var mapped = Map(schema, o.AccessorPrefix + Naming.Pascal(o.Name) + "Response");
            // Free-form JSON (no schema, or an object with arbitrary keys) is returned as a JsonElement.
            var freeForm = mapped.Type == TypeRef.Object || mapped.Type.Decl == TypeRef.MapOf(TypeRef.Object, true).Decl;
            var response = freeForm ? TypeRef.JsonElement : mapped.Type;
            _rootTypes.Add(response.Runtime);
            return new(ResponseKind.Json, response, "application/json", body, bodyNullable, types);
        }

        var accept = string.Join(", ", types) + (types.Any(IsJson) ? "" : ", application/json;q=0.5");
        var kind = types.All(IsText) ? ResponseKind.Text : ResponseKind.Content;
        return new(kind, null, accept, body, bodyNullable, types);
    }

    // ------------------------------------------------------------------ rendering

    private string Header()
    {
        var version = _root["info"]?["version"]?.GetValue<string>() ?? "unknown";
        return "// <auto-generated>\n"
            + $"//     Generated by tools/FreeAstroApi.Generator from spec/freeastro.json (API version {version}).\n"
            + "//     Do not edit by hand: run scripts/generate.sh.\n"
            + "// </auto-generated>\n\n#nullable enable\n";
    }

    private string RenderModels()
    {
        var w = new CodeWriter(Header());
        w.Line("namespace FreeAstroApi.Models");
        w.Open();
        var first = true;
        foreach (var model in _models)
        {
            if (!first) w.Line();
            first = false;
            var doc = Docs.ForSchema(model.Schema, _schemas);
            if (doc.Count == 0) doc = [model.Origin == "inline schema" ? $"Generated model <c>{model.Name}</c>." : $"The <c>{Docs.Escape(model.Origin)}</c> schema."];
            w.Summary(doc);
            w.Line($"public partial class {model.Name}");
            w.Open();
            var names = new HashSet<string>(model.Properties.Select(p => p.CsName), StringComparer.Ordinal);
            foreach (var p in model.Properties)
            {
                w.Summary(p.Doc);
                w.Line($"[global::System.Text.Json.Serialization.JsonPropertyName({Literal(p.JsonName)})]");
                var init = p.Optional || p.Type.IsValueType ? "" : p.Type.IsCollection ? " = new();" : " = default!;";
                w.Line($"public {p.Type.Annotated(p.Optional)} {p.CsName} {{ get; set; }}{init}");
                w.Line();
            }
            var extension = !names.Contains("AdditionalProperties") ? "AdditionalProperties" : !names.Contains("ExtensionData") ? "ExtensionData" : null;
            if (extension is not null)
            {
                w.Summary(["Fields returned by the API that this SDK version does not model yet."]);
                w.Line("[global::System.Text.Json.Serialization.JsonExtensionData]");
                w.Line($"public global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement>? {extension} {{ get; set; }}");
            }
            else
            {
                w.TrimBlankLine();
            }
            w.Close();
        }
        w.Close();
        return w.ToString();
    }

    private string RenderParameters(List<OperationDef> ops, Dictionary<OperationDef, string> classes)
    {
        var w = new CodeWriter(Header());
        w.Line("namespace FreeAstroApi.Models");
        w.Open();
        var first = true;
        foreach (var o in ops.Where(classes.ContainsKey))
        {
            if (!first) w.Line();
            first = false;
            var name = classes[o];
            w.Summary([$"Query parameters for <c>{o.Method} {Docs.Escape(o.Path)}</c> (<c>client.{o.Accessor}</c>)."]);
            w.Line($"public sealed partial class {name}");
            w.Open();
            var taken = new HashSet<string>(StringComparer.Ordinal) { name, "WriteTo" };
            var props = new List<(JsonObject Param, string CsName, Mapped Type, bool Required)>();
            foreach (var p in o.QueryParams)
            {
                var jsonName = p["name"]!.GetValue<string>();
                var csName = Naming.Unique(Naming.Pascal(jsonName), taken);
                var mapped = Map(p["schema"], name + Naming.Pascal(jsonName));
                var required = p["required"]?.GetValue<bool>() == true;
                var doc = Docs.ForParameter(p, _schemas);
                if (doc.Count == 0) doc = [$"The <c>{Docs.Escape(jsonName)}</c> query parameter."];
                w.Summary(doc);
                w.Line($"[global::System.Text.Json.Serialization.JsonPropertyName({Literal(jsonName)})]");
                var optional = !required || mapped.Nullable;
                var init = optional || mapped.Type.IsValueType ? "" : mapped.Type.IsCollection ? " = new();" : " = default!;";
                w.Line($"public {mapped.Type.Annotated(optional)} {csName} {{ get; set; }}{init}");
                w.Line();
                props.Add((p, csName, mapped, required && !mapped.Nullable));
            }
            w.Line("internal void WriteTo(global::FreeAstroApi.Internal.QueryBuilder query)");
            w.Open();
            foreach (var (p, csName, _, required) in props)
            {
                w.Line($"query.{(required ? "AddRequired" : "Add")}({Literal(p["name"]!.GetValue<string>())}, {csName});");
            }
            w.Close();
            w.Close();
        }
        w.Close();
        return w.ToString();
    }

    private string RenderResources(List<OperationDef> ops, Dictionary<OperationDef, string> paramsClasses, Dictionary<OperationDef, Signature> signatures)
    {
        var w = new CodeWriter(Header());
        var resources = ops.GroupBy(o => o.Resource).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

        w.Line("namespace FreeAstroApi");
        w.Open();
        w.Line("public sealed partial class FreeAstroClient");
        w.Open();
        foreach (var r in resources)
        {
            w.Summary([$"<c>{Docs.Escape(r.Key)}</c> operations ({Describe(r)})."]);
            w.Line($"public global::FreeAstroApi.Resources.{ResourceClass(r.Key, null)} {Naming.Pascal(r.Key)} {{ get; private set; }} = default!;");
            w.Line();
        }
        w.Line("private void InitializeResources(global::FreeAstroApi.Internal.FreeAstroTransport transport)");
        w.Open();
        foreach (var r in resources) w.Line($"{Naming.Pascal(r.Key)} = new global::FreeAstroApi.Resources.{ResourceClass(r.Key, null)}(transport);");
        w.Close();
        w.Close();
        w.Close();
        w.Line();

        w.Line("namespace FreeAstroApi.Resources");
        w.Open();
        var firstClass = true;
        foreach (var r in resources)
        {
            var subs = r.Where(o => o.Sub is not null).GroupBy(o => o.Sub!).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            var methods = r.Where(o => o.Sub is null).ToList();
            foreach (var sub in subs)
            {
                if (!firstClass) w.Line();
                firstClass = false;
                RenderResourceClass(w, ResourceClass(r.Key, sub.Key), $"<c>{Docs.Escape(r.Key)}.{Docs.Escape(sub.Key)}</c> operations ({Describe(sub)}).", [], sub.ToList(), paramsClasses, signatures);
            }
            if (!firstClass) w.Line();
            firstClass = false;
            var children = subs.Select(s => (Naming.Pascal(s.Key), ResourceClass(r.Key, s.Key), $"<c>{Docs.Escape(r.Key)}.{Docs.Escape(s.Key)}</c> operations.")).ToList();
            RenderResourceClass(w, ResourceClass(r.Key, null), $"<c>{Docs.Escape(r.Key)}</c> operations ({Describe(r)}).", children, methods, paramsClasses, signatures);
        }
        w.Close();
        return w.ToString();
    }

    private static string Describe(IEnumerable<OperationDef> ops)
    {
        var list = ops.ToList();
        var paths = list.Select(o => o.Path).ToList();
        return list.Count == 1 ? $"<c>{Docs.Escape(paths[0])}</c>" : $"{list.Count} operations";
    }

    private static string ResourceClass(string resource, string? sub) =>
        Naming.Pascal(resource) + (sub is null ? "" : Naming.Pascal(sub)) + "Resource";

    private void RenderResourceClass(
        CodeWriter w,
        string className,
        string summary,
        List<(string Property, string Class, string Doc)> children,
        List<OperationDef> methods,
        Dictionary<OperationDef, string> paramsClasses,
        Dictionary<OperationDef, Signature> signatures)
    {
        w.Summary([summary]);
        w.Line($"public sealed partial class {className}");
        w.Open();
        if (methods.Count > 0)
        {
            w.Line("private readonly global::FreeAstroApi.Internal.FreeAstroTransport _transport;");
            w.Line();
        }
        w.Line($"internal {className}(global::FreeAstroApi.Internal.FreeAstroTransport transport)");
        w.Open();
        if (methods.Count > 0) w.Line("_transport = transport;");
        foreach (var c in children) w.Line($"{c.Property} = new {c.Class}(transport);");
        w.Close();
        foreach (var c in children)
        {
            w.Line();
            w.Summary([c.Doc]);
            w.Line($"public {c.Class} {c.Property} {{ get; }}");
        }
        foreach (var o in methods)
        {
            w.Line();
            RenderMethod(w, o, paramsClasses.GetValueOrDefault(o), signatures[o]);
        }
        w.Close();
    }

    private void RenderMethod(CodeWriter w, OperationDef o, string? paramsClass, Signature sig)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "body", "parameters", "options", "cancellationToken", "request" };
        var required = new List<(string Decl, string Name, string Doc, bool NullCheck)>();
        var optional = new List<(string Decl, string Name, string Doc)>();

        var pathArgs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in o.PathParams)
        {
            var jsonName = p["name"]!.GetValue<string>();
            var argName = Naming.Unique(Naming.Camel(jsonName), reserved);
            pathArgs[jsonName] = argName;
            var mapped = Map(p["schema"], "Path");
            var t = mapped.Type == TypeRef.Object ? TypeRef.String : mapped.Type;
            var doc = Docs.ForParameter(p, _schemas);
            required.Add((t.Decl, argName, doc.Count > 0 ? string.Join(" ", doc) : $"The <c>{Docs.Escape(jsonName)}</c> path parameter.", !t.IsValueType));
        }
        if (sig.Body is not null)
        {
            if (o.BodyRequired) required.Add((sig.Body.Decl, "body", "The request body.", !sig.Body.IsValueType));
            else optional.Add((sig.Body.Annotated(true), "body", "The request body (optional)."));
        }
        if (paramsClass is not null)
        {
            var paramsRequired = o.QueryParams.Any(p => p["required"]?.GetValue<bool>() == true);
            var doc = "Query parameters.";
            if (paramsRequired) required.Add(($"global::FreeAstroApi.Models.{paramsClass}", "parameters", doc, true));
            else optional.Add(($"global::FreeAstroApi.Models.{paramsClass}?", "parameters", doc));
        }

        var returnType = sig.Kind switch
        {
            ResponseKind.Json => $"global::System.Threading.Tasks.Task<{sig.Response!.Decl}>",
            ResponseKind.Text => "global::System.Threading.Tasks.Task<string>",
            ResponseKind.Content => "global::System.Threading.Tasks.Task<global::FreeAstroApi.FreeAstroContent>",
            _ => "global::System.Threading.Tasks.Task",
        };
        var returnsDoc = sig.Kind switch
        {
            ResponseKind.Json when sig.Response == TypeRef.JsonElement => "The JSON response. The API does not publish a schema for it yet.",
            ResponseKind.Json => $"The deserialized <c>{Docs.Escape(sig.Response!.ModelName ?? ShortName(sig.Response!.Decl))}</c> response.",
            ResponseKind.Text => $"The response body as text ({Docs.Escape(string.Join(", ", sig.ContentTypes))}).",
            ResponseKind.Content => $"The raw response body and its content type ({Docs.Escape(string.Join(", ", sig.ContentTypes))}).",
            _ => "A task that completes when the request succeeds.",
        };

        var summary = o.Op["summary"]?.GetValue<string>() ?? o.Id;
        var description = (o.Op["description"]?.GetValue<string>() ?? "").Trim().Replace("\r", "", StringComparison.Ordinal).Split("\n\n")[0].Trim();
        w.Summary(Docs.Lines(summary));
        w.Line("/// <remarks>");
        if (description.Length > 0 && description != summary)
        {
            w.Line("/// <para>");
            foreach (var line in Docs.Lines(description)) w.Line("/// " + line);
            w.Line("/// </para>");
        }
        w.Line($"/// <para><c>{o.Method} {Docs.Escape(o.Path)}</c> (operation <c>{Docs.Escape(o.Id)}</c>).</para>");
        if (o.Deprecated) w.Line("/// <para>Deprecated by FreeAstroAPI.</para>");
        w.Line("/// </remarks>");
        foreach (var a in required) w.Line($"/// <param name=\"{a.Name.TrimStart('@')}\">{a.Doc}</param>");
        foreach (var a in optional) w.Line($"/// <param name=\"{a.Name.TrimStart('@')}\">{a.Doc}</param>");
        w.Line("/// <param name=\"options\">Per-request options: headers, idempotency key, timeout and retries.</param>");
        w.Line("/// <param name=\"cancellationToken\">A token that cancels the request.</param>");
        w.Line($"/// <returns>{returnsDoc}</returns>");
        w.Line("/// <exception cref=\"global::FreeAstroApi.FreeAstroApiException\">The API returned an error status code.</exception>");
        w.Line("/// <exception cref=\"global::FreeAstroApi.FreeAstroNetworkException\">The API could not be reached or timed out.</exception>");
        if (o.Deprecated) w.Line($"[global::System.Obsolete({Literal($"{o.Method} {o.Path} is deprecated by FreeAstroAPI.")})]");

        var args = required.Select(a => $"{a.Decl} {a.Name}")
            .Concat(optional.Select(a => $"{a.Decl} {a.Name} = null"))
            .Append("global::FreeAstroApi.RequestOptions? options = null")
            .Append("global::System.Threading.CancellationToken cancellationToken = default");
        w.Line($"public {returnType} {o.MethodName}({string.Join(", ", args)})");
        w.Open();
        foreach (var a in required.Where(a => a.NullCheck)) w.Line($"global::FreeAstroApi.Internal.Guard.NotNull({a.Name}, nameof({a.Name}));");

        var pathExpr = new StringBuilder();
        var pos = 0;
        var parts = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(o.Path, "\\{([^}]+)\\}"))
        {
            if (m.Index > pos) parts.Add(Literal(o.Path[pos..m.Index]));
            if (!pathArgs.TryGetValue(m.Groups[1].Value, out var arg)) throw new GeneratorException($"{o.Id}: path parameter {m.Groups[1].Value} is not declared");
            parts.Add($"global::FreeAstroApi.Internal.PathEncoder.Encode({arg})");
            pos = m.Index + m.Length;
        }
        if (pos < o.Path.Length) parts.Add(Literal(o.Path[pos..]));
        pathExpr.Append(string.Join(" + ", parts));

        w.Line($"var request = new global::FreeAstroApi.Internal.FreeAstroRequest({Literal(o.Method)}, {pathExpr}, {Literal(sig.Accept)});");
        if (sig.Body is not null) w.Line(o.BodyRequired ? "request.SetJsonBody(body);" : "if (body is not null) request.SetJsonBody(body);");
        if (paramsClass is not null)
        {
            var isRequired = required.Any(a => a.Name == "parameters");
            w.Line(isRequired ? "parameters.WriteTo(request.Query);" : "parameters?.WriteTo(request.Query);");
        }
        var send = sig.Kind switch
        {
            ResponseKind.Json => $"_transport.SendJsonAsync<{sig.Response!.Decl}>(request, options, cancellationToken)",
            ResponseKind.Text => "_transport.SendTextAsync(request, options, cancellationToken)",
            ResponseKind.Content => "_transport.SendContentAsync(request, options, cancellationToken)",
            _ => "_transport.SendNoContentAsync(request, options, cancellationToken)",
        };
        w.Line($"return {send};");
        w.Close();
    }

    private static string ShortName(string decl) => decl.Replace("global::System.Collections.Generic.", "", StringComparison.Ordinal)
        .Replace("global::FreeAstroApi.Models.", "", StringComparison.Ordinal)
        .Replace("global::System.Text.Json.", "", StringComparison.Ordinal);

    private string RenderOperations(List<OperationDef> ops)
    {
        var w = new CodeWriter(Header());
        w.Line("namespace FreeAstroApi");
        w.Open();
        w.Line("public static partial class FreeAstroOperations");
        w.Open();
        w.Summary([$"Every public operation in the vendored OpenAPI document ({ops.Count} operations) and the client method that calls it."]);
        w.Line("public static global::System.Collections.Generic.IReadOnlyList<global::FreeAstroApi.FreeAstroOperation> All { get; } = new global::FreeAstroApi.FreeAstroOperation[]");
        w.Open();
        foreach (var o in ops)
        {
            w.Line($"new({Literal(o.Id)}, {Literal(o.Method)}, {Literal(o.Path)}, {Literal(o.Accessor)}, {(o.Deprecated ? "true" : "false")}),");
        }
        w.CloseWith("};");
        w.Close();
        w.Close();
        return w.ToString();
    }

    private string RenderJsonContext()
    {
        var w = new CodeWriter(Header());
        var types = new SortedSet<string>(StringComparer.Ordinal)
        {
            "string", "int", "long", "double", "bool", "object",
            "global::System.Text.Json.JsonElement",
            "global::System.Collections.Generic.Dictionary<string, object>",
            "global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement>",
            "global::System.Collections.Generic.List<object>",
        };
        types.UnionWith(_rootTypes);
        w.Line("namespace FreeAstroApi.Serialization");
        w.Open();
        w.Line("[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(");
        w.Line("    DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,");
        w.Line("    GenerationMode = global::System.Text.Json.Serialization.JsonSourceGenerationMode.Metadata)]");
        foreach (var t in types) w.Line($"[global::System.Text.Json.Serialization.JsonSerializable(typeof({t}))]");
        w.Line("internal sealed partial class FreeAstroJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext");
        w.Line("{");
        w.Line("}");
        w.Close();
        return w.ToString();
    }

    private static string Literal(string value) => SymbolDisplay(value);

    private static string SymbolDisplay(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
