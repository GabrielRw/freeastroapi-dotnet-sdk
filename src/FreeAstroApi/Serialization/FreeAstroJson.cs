using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FreeAstroApi.Serialization;

/// <summary>The JSON settings the client uses for requests and responses.</summary>
/// <remarks>
/// Model metadata comes from a System.Text.Json source-generated context; other types fall back to
/// reflection. Use these options to turn a <see cref="JsonElement"/> result into a model, for example
/// <c>element.Deserialize&lt;NatalChartRequest&gt;(FreeAstroJson.Options)</c>.
/// </remarks>
public static class FreeAstroJson
{
    /// <summary>Read-only serializer options: null properties are omitted, names come from the API.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(FreeAstroJsonContext.Default, new DefaultJsonTypeInfoResolver()),
        };
        options.MakeReadOnly();
        return options;
    }

    internal static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
}
