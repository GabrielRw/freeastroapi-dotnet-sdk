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
    /// method; 408/5xx responses, timeouts and network failures are retried for GET requests and for requests
    /// that carry an idempotency key or opt in with <see cref="RequestOptions.Retry"/>.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Delay before the first retry; it doubles on each attempt. Defaults to 500 ms.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Longest delay the client waits before a retry. A <c>Retry-After</c> header asking for longer is not
    /// waited out: the error is thrown so the caller can decide. Defaults to 30 seconds.
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
            MaxRetryDelay = MaxRetryDelay,
        };
        foreach (var header in DefaultHeaders) copy.DefaultHeaders[header.Key] = header.Value;
        return copy;
    }
}
