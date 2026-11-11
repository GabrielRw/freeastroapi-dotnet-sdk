using System;
using System.Collections.Generic;

namespace FreeAstroApi;

/// <summary>Options for a single request.</summary>
public sealed class RequestOptions
{
    /// <summary>Extra headers for this request.</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sent as the <c>Idempotency-Key</c> header. A request with a key is safe to replay, so it is retried
    /// like a GET request.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Overrides <see cref="FreeAstroClientOptions.Timeout"/> for this request.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Overrides <see cref="FreeAstroClientOptions.MaxRetries"/> for this request.</summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// <see langword="true"/> retries this request even if it is not a GET and has no idempotency key (use it
    /// for calculations that are safe to replay); <see langword="false"/> disables retries; <see langword="null"/>
    /// (the default) applies the standard policy.
    /// </summary>
    public bool? Retry { get; set; }
}
