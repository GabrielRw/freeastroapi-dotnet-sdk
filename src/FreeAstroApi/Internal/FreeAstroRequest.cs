using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FreeAstroApi.Serialization;

namespace FreeAstroApi.Internal;

/// <summary>A request built by a generated resource method, replayable across retries.</summary>
internal sealed class FreeAstroRequest
{
    public FreeAstroRequest(string method, string path, string accept, string? operationId = null)
    {
        Method = method;
        Path = path;
        Accept = accept;
        OperationId = operationId;
    }

    public string Method { get; }

    /// <summary>The OpenAPI operationId, or <see langword="null"/> for <c>SendAsync</c>/<c>SendRawAsync</c> calls.</summary>
    public string? OperationId { get; }

    public string Path { get; }

    public string Accept { get; }

    public QueryBuilder Query { get; } = new();

    public byte[]? Body { get; private set; }

    public void SetJsonBody<T>(T body) => Body = JsonSerializer.SerializeToUtf8Bytes(body, FreeAstroJson.TypeInfo<T>());

    public void SetJsonBody(object body) => Body = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), FreeAstroJson.Options);
}

/// <summary>Builds a form-style (exploded) query string.</summary>
internal sealed class QueryBuilder
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];

    public int Count => _pairs.Count;

    public void AddRequired(string name, object? value)
    {
        if (value is null) throw new ArgumentException($"The query parameter '{name}' is required.", name);
        Add(name, value);
    }

    public void Add(string name, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                _pairs.Add(new(name, text));
                return;
            case IEnumerable items:
                foreach (var item in items)
                {
                    if (item is not null) _pairs.Add(new(name, Format(item)));
                }
                return;
            default:
                _pairs.Add(new(name, Format(value)));
                return;
        }
    }

    internal static string Format(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public override string ToString()
    {
        if (_pairs.Count == 0) return "";
        var sb = new StringBuilder("?");
        for (var i = 0; i < _pairs.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(_pairs[i].Key)).Append('=').Append(Uri.EscapeDataString(_pairs[i].Value));
        }
        return sb.ToString();
    }
}

internal static class PathEncoder
{
    public static string Encode(object value) => Uri.EscapeDataString(QueryBuilder.Format(value));
}

internal static class Guard
{
    public static void NotNull(object? value, string name)
    {
        if (value is null) throw new ArgumentNullException(name);
    }
}
