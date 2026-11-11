using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FreeAstroApi.Internal;

/// <summary>Maps an error response to the matching <see cref="FreeAstroApiException"/> subclass.</summary>
internal static class ErrorMapper
{
    public static async Task<FreeAstroApiException> CreateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? body = null;
        JsonElement? json = null;
        try
        {
            var bytes = await FreeAstroTransport.ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false);
            body = Encoding.UTF8.GetString(bytes);
            if (bytes.Length > 0 && (bytes[0] == (byte)'{' || bytes[0] == (byte)'[' || char.IsWhiteSpace((char)bytes[0])))
            {
                using var document = JsonDocument.Parse(bytes);
                json = document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            json = null;
        }
        catch (HttpRequestException)
        {
            body = null;
        }

        var status = (int)response.StatusCode;
        var details = new FreeAstroErrorDetails
        {
            StatusCode = status,
            Body = body,
            Error = json,
            ErrorCode = ErrorCode(json),
            RequestId = Header(response, "x-request-id") ?? StringProperty(json, "trace_id") ?? StringProperty(json, "request_id"),
            RetryAfter = FreeAstroTransport.RetryAfter(response),
        };
        var message = $"FreeAstroAPI returned {status}: {Message(json, response)}";
        if (details.RequestId is not null) message += $" (request id {details.RequestId})";

        return status switch
        {
            400 or 422 => new FreeAstroValidationException(message, details),
            401 => new FreeAstroAuthenticationException(message, details),
            403 => new FreeAstroPermissionException(message, details),
            404 => new FreeAstroNotFoundException(message, details),
            409 => new FreeAstroConflictException(message, details),
            429 => new FreeAstroRateLimitException(message, details),
            >= 500 => new FreeAstroServerException(message, details),
            _ => new FreeAstroApiException(message, details),
        };
    }

    private static string Message(JsonElement? json, HttpResponseMessage response)
    {
        if (json is { ValueKind: JsonValueKind.Object } error)
        {
            if (error.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString())) return detail.GetString()!;
                if (detail.ValueKind == JsonValueKind.Array)
                {
                    var issues = detail.EnumerateArray().Select(ValidationIssue).Where(s => s.Length > 0).ToList();
                    if (issues.Count > 0) return string.Join("; ", issues);
                }
            }
            foreach (var name in new[] { "message", "error", "title" })
            {
                if (StringProperty(json, name) is { Length: > 0 } text) return text;
            }
        }
        return string.IsNullOrEmpty(response.ReasonPhrase) ? "request failed" : response.ReasonPhrase!;
    }

    // FastAPI validation errors: {"detail": [{"loc": ["body", "year"], "msg": "...", "type": "..."}]}
    private static string ValidationIssue(JsonElement issue)
    {
        if (issue.ValueKind != JsonValueKind.Object) return issue.ValueKind == JsonValueKind.String ? issue.GetString() ?? "" : "";
        var msg = issue.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
        if (issue.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array)
        {
            var parts = loc.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText()).ToList();
            if (parts.Count > 0) return $"{string.Join(".", parts)}: {msg}";
        }
        return msg;
    }

    private static string? ErrorCode(JsonElement? json)
    {
        if (StringProperty(json, "code") is { Length: > 0 } code) return code;
        // {"error": "invalid_idempotency_key", "detail": "..."}: `error` is the code when `detail` explains it.
        if (json is { ValueKind: JsonValueKind.Object } error && error.TryGetProperty("detail", out _)) return StringProperty(json, "error");
        return null;
    }

    private static string? StringProperty(JsonElement? json, string name) =>
        json is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
}
