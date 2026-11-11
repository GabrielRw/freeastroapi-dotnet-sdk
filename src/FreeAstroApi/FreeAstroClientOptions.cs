using System;
using System.Collections.Generic;

namespace FreeAstroApi;

/// <summary>Configuration for <see cref="FreeAstroClient"/>.</summary>
public sealed class FreeAstroClientOptions
{
    /// <summary>The production API endpoint.</summary>
    public const string DefaultBaseUrl = "https://api.freeastroapi.com";

    /// <summary>
    /// Your FreeAstroAPI key, sent in the <c>x-api-key</c> header. When it is not set the client reads the
    /// <c>FREEASTRO_API_KEY</c> environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The API base address. Defaults to <see cref="DefaultBaseUrl"/>.</summary>
    public Uri BaseUrl { get; set; } = new(DefaultBaseUrl);

    /// <summary>Timeout for a single attempt. Defaults to 30 seconds. Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a failed request is retried. Defaults to 2. HTTP 429 responses are retried for every
    /// operation; 408/500/502/503/504 responses, timeouts and network failures are retried for every operation
    /// except the side-effecting ones in <see cref="FreeAstroOperations.SideEffectingOperationIds"/>, which are
    /// retried only with an idempotency key or <see cref="RequestOptions.Retry"/>. Other 4xx are never retried.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Base of the exponential backoff: retry <c>n</c> waits a random time between 0 and
    /// <c>InitialRetryDelay * 2^n</c> (full jitter), capped at <see cref="MaxBackoff"/>. Defaults to 500 ms.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound of the computed backoff before jitter. Defaults to 8 seconds.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Longest server-requested wait the client honours. When <c>Retry-After</c> or <c>retry_after_ms</c> asks
    /// for longer, the client does not wait: it throws the error (with
    /// <see cref="FreeAstroApiException.RetryAfter"/>) so the caller can decide. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Headers added to every request.</summary>
    public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal FreeAstroClientOptions Snapshot()
    {
        var copy = new FreeAstroClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = BaseUrl,
            Timeout = Timeout,
            MaxRetries = MaxRetries,
            InitialRetryDelay = InitialRetryDelay,
            MaxBackoff = MaxBackoff,
            MaxRetryDelay = MaxRetryDelay,
        };
        foreach (var header in DefaultHeaders) copy.DefaultHeaders[header.Key] = header.Value;
        return copy;
    }
}
