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
}
