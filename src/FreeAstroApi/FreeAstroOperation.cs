using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeAstroApi;

/// <summary>An API operation and the client method that calls it.</summary>
public sealed class FreeAstroOperation
{
    /// <summary>Creates an operation descriptor.</summary>
    /// <param name="operationId">The OpenAPI operationId.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path template.</param>
    /// <param name="accessor">The client member path, for example <c>Western.Natal.InsightsAsync</c>.</param>
    /// <param name="deprecated">Whether the operation is deprecated.</param>
    public FreeAstroOperation(string operationId, string method, string path, string accessor, bool deprecated)
    {
        OperationId = operationId;
        Method = method;
        Path = path;
        Accessor = accessor;
        Deprecated = deprecated;
    }

    /// <summary>The OpenAPI operationId, for example <c>v1_western_natal_insights</c>.</summary>
    public string OperationId { get; }

    /// <summary>The HTTP method.</summary>
    public string Method { get; }

    /// <summary>The path template, for example <c>/api/v1/chinese/calendar/{date}</c>.</summary>
    public string Path { get; }

    /// <summary>The client member path, for example <c>Western.Natal.InsightsAsync</c>.</summary>
    public string Accessor { get; }

    /// <summary>Whether FreeAstroAPI marks the operation as deprecated.</summary>
    public bool Deprecated { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Method} {Path} -> client.{Accessor}";
}

/// <summary>The registry of every public API operation, generated from the vendored OpenAPI document.</summary>
public static partial class FreeAstroOperations
{
    private static readonly HashSet<string> SideEffectingIds = new(StringComparer.Ordinal)
    {
        "v1_natal_calculate", // may create a paid report job
        "v1_ai_generate",
        "v2_vedic_chat",
        "v2_western_chat",
        "v2_vedic_qa",
        "v1_western_transits_timeline",
        "v1_western_transits_insights",
    };

    private static Dictionary<string, FreeAstroOperation>? s_byRoute;

    /// <summary>
    /// The operationIds that can have side effects (billing, report jobs, LLM generation). For these, HTTP 408/5xx
    /// responses, timeouts and network failures are retried only when the request carries an
    /// <see cref="RequestOptions.IdempotencyKey"/> or opts in with <see cref="RequestOptions.Retry"/>; every other
    /// operation is retried automatically. HTTP 429 is retried for every operation.
    /// </summary>
    public static IReadOnlyCollection<string> SideEffectingOperationIds => SideEffectingIds;

    /// <summary>Whether <paramref name="operationId"/> is in <see cref="SideEffectingOperationIds"/>.</summary>
    /// <param name="operationId">An OpenAPI operationId.</param>
    /// <returns><see langword="true"/> for a side-effecting operation.</returns>
    public static bool IsSideEffecting(string operationId) => SideEffectingIds.Contains(operationId);

    /// <summary>Finds the operation for a concrete method and path (path templates with parameters never match).</summary>
    internal static FreeAstroOperation? FindByRoute(string method, string path)
    {
        var byRoute = s_byRoute ??= All.GroupBy(o => o.Method + " " + o.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return byRoute.TryGetValue(method + " " + path, out var operation) ? operation : null;
    }
}
