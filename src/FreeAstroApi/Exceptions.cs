using System;
using System.Text.Json;

namespace FreeAstroApi;

/// <summary>Base class for every error raised by the FreeAstroAPI client.</summary>
public class FreeAstroException : Exception
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The API could not be reached (DNS, connection or TLS failure).</summary>
public class FreeAstroNetworkException : FreeAstroException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroNetworkException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroNetworkException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The request did not complete within the configured timeout.</summary>
public class FreeAstroTimeoutException : FreeAstroNetworkException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroTimeoutException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A successful response could not be deserialized into the expected type.</summary>
public class FreeAstroDeserializationException : FreeAstroException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroDeserializationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroDeserializationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroDeserializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for a response body.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="body">The response body.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroDeserializationException(string message, string? body, Exception innerException)
        : base(message, innerException)
    {
        Body = body;
    }

    /// <summary>The response body that failed to deserialize.</summary>
    public string? Body { get; }
}

/// <summary>The API answered with an error status code.</summary>
public class FreeAstroApiException : FreeAstroException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroApiException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroApiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroApiException(string message, FreeAstroErrorDetails details)
        : base(message)
    {
        FreeAstroApi.Internal.Guard.NotNull(details, nameof(details));
        StatusCode = details.StatusCode;
        Body = details.Body;
        RequestId = details.RequestId;
        ErrorCode = details.ErrorCode;
        Error = details.Error;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body.</summary>
    public string? Body { get; }

    /// <summary>The request id from the <c>x-request-id</c> header or the error body (<c>trace_id</c>), for support.</summary>
    public string? RequestId { get; }

    /// <summary>The machine-readable error code from the body (<c>code</c>, or <c>error</c> next to <c>detail</c>), if any.</summary>
    public string? ErrorCode { get; }

    /// <summary>The parsed JSON error body, if the body was JSON.</summary>
    public JsonElement? Error { get; }
}

/// <summary>HTTP 400 or 422: the request was rejected as invalid.</summary>
public class FreeAstroValidationException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroValidationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroValidationException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>HTTP 401: the API key is missing or invalid.</summary>
public class FreeAstroAuthenticationException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroAuthenticationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroAuthenticationException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>HTTP 403: the API key is not allowed to call this operation (plan or credit limits).</summary>
public class FreeAstroPermissionException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroPermissionException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroPermissionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroPermissionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroPermissionException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>HTTP 404: the resource does not exist.</summary>
public class FreeAstroNotFoundException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroNotFoundException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroNotFoundException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>HTTP 409: the request conflicts with another one (for example a reused idempotency key).</summary>
public class FreeAstroConflictException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroConflictException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroConflictException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>HTTP 429: too many requests. Thrown once retries are exhausted.</summary>
public class FreeAstroRateLimitException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroRateLimitException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroRateLimitException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroRateLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroRateLimitException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
        RetryAfter = details?.RetryAfter;
    }

    /// <summary>How long the API asked to wait (<c>Retry-After</c>), if it said.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>HTTP 5xx: the API failed. Thrown once retries are exhausted.</summary>
public class FreeAstroServerException : FreeAstroApiException
{
    /// <summary>Creates an exception.</summary>
    public FreeAstroServerException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public FreeAstroServerException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public FreeAstroServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">The response details.</param>
    public FreeAstroServerException(string message, FreeAstroErrorDetails details)
        : base(message, details)
    {
    }
}

/// <summary>Details of an error response, used to construct <see cref="FreeAstroApiException"/> and its subclasses.</summary>
public sealed class FreeAstroErrorDetails
{
    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>The raw response body.</summary>
    public string? Body { get; set; }

    /// <summary>The request id, if known.</summary>
    public string? RequestId { get; set; }

    /// <summary>The machine-readable error code, if any.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>The parsed JSON error body, if any.</summary>
    public JsonElement? Error { get; set; }

    /// <summary>The <c>Retry-After</c> delay, if any.</summary>
    public TimeSpan? RetryAfter { get; set; }
}
