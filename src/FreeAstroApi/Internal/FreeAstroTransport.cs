using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FreeAstroApi.Serialization;

namespace FreeAstroApi.Internal;

/// <summary>Sends requests: authentication, headers, timeouts, retries and error mapping.</summary>
internal sealed class FreeAstroTransport
{
    private static readonly int[] RetryStatuses = [408, 429, 500, 502, 503, 504];

    private readonly HttpClient _http;
    private readonly FreeAstroClientOptions _options;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public FreeAstroTransport(HttpClient http, FreeAstroClientOptions options, string apiKey)
    {
        _http = http;
        _options = options;
        _apiKey = apiKey;
        _baseUrl = options.BaseUrl.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>Waits between retries; replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, token) => Task.Delay(delay, token);

    public async Task<T> SendJsonAsync<T>(FreeAstroRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0) return default!;
        try
        {
            return JsonSerializer.Deserialize(bytes, FreeAstroJson.TypeInfo<T>())!;
        }
        catch (JsonException ex)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new FreeAstroDeserializationException(
                $"The {request.Method} {request.Path} response could not be read as {typeof(T).Name}: {ex.Message}", body, ex);
        }
    }

    public async Task<string> SendTextAsync(FreeAstroRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, cancellationToken).ConfigureAwait(false);
        return ToContent(response, await ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false)).ReadAsString();
    }

    public async Task<FreeAstroContent> SendContentAsync(FreeAstroRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, cancellationToken).ConfigureAwait(false);
        return ToContent(response, await ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false));
    }

    public async Task SendNoContentAsync(FreeAstroRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, cancellationToken).ConfigureAwait(false);
    }

    private static FreeAstroContent ToContent(HttpResponseMessage response, byte[] bytes)
    {
        var type = response.Content.Headers.ContentType;
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');
        return new FreeAstroContent(bytes, type?.MediaType, type?.CharSet, fileName);
    }

    private async Task<HttpResponseMessage> SendAsync(FreeAstroRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        var maxRetries = options?.Retry == false ? 0 : Math.Max(0, options?.MaxRetries ?? _options.MaxRetries);
        var replaySafe = request.Method is "GET" or "HEAD" or "OPTIONS"
            || !string.IsNullOrEmpty(options?.IdempotencyKey)
            || options?.Retry == true;
        var timeout = options?.Timeout ?? _options.Timeout;
        var uri = new Uri(_baseUrl + request.Path + request.Query, UriKind.Absolute);

        for (var attempt = 0; ; attempt++)
        {
            var canRetry = attempt < maxRetries;
            HttpResponseMessage response;
            using (var message = CreateMessage(request, uri, options))
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (timeout != Timeout.InfiniteTimeSpan) timeoutSource.CancelAfter(timeout);
                try
                {
                    response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (replaySafe && canRetry)
                    {
                        await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new FreeAstroTimeoutException($"The FreeAstroAPI request {request.Method} {request.Path} timed out after {timeout.TotalSeconds:0.###}s.", ex);
                }
                catch (HttpRequestException ex)
                {
                    if (replaySafe && canRetry)
                    {
                        await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new FreeAstroNetworkException($"Unable to reach FreeAstroAPI ({request.Method} {request.Path}): {ex.Message}", ex);
                }
            }

            if (response.IsSuccessStatusCode) return response;

            var status = (int)response.StatusCode;
            // A 429 means the request was rejected before it ran, so it is safe to replay for any method.
            if (canRetry && (status == 429 || (replaySafe && RetryStatuses.Contains(status))))
            {
                var delay = RetryAfter(response) ?? Backoff(attempt);
                if (delay <= _options.MaxRetryDelay)
                {
                    response.Dispose();
                    await Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            using (response)
            {
                throw await ErrorMapper.CreateAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private HttpRequestMessage CreateMessage(FreeAstroRequest request, Uri uri, RequestOptions? options)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        var userAgent = FreeAstroClient.UserAgent;
        var accept = request.Accept;
        foreach (var header in Merge(_options.DefaultHeaders, options?.Headers))
        {
            if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) userAgent = header.Value + " " + FreeAstroClient.UserAgent;
            else if (header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) accept = header.Value;
            else if (!header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        message.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        message.Headers.TryAddWithoutValidation("Accept", accept);
        message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (!string.IsNullOrEmpty(options?.IdempotencyKey))
        {
            message.Headers.Remove("Idempotency-Key");
            message.Headers.TryAddWithoutValidation("Idempotency-Key", options!.IdempotencyKey);
        }
        if (request.Body is not null)
        {
            var content = new ByteArrayContent(request.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            message.Content = content;
        }
        return message;
    }

    private static IEnumerable<KeyValuePair<string, string>> Merge(IDictionary<string, string> defaults, IDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return defaults;
        var merged = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var header in overrides) merged[header.Key] = header.Value;
        return merged;
    }

    private TimeSpan Backoff(int attempt)
    {
        var ticks = _options.InitialRetryDelay.Ticks * Math.Pow(2, Math.Min(attempt, 30));
        return ticks >= _options.MaxRetryDelay.Ticks ? _options.MaxRetryDelay : TimeSpan.FromTicks((long)ticks);
    }

    internal static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }

    internal static Task<byte[]> ReadBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NET
        return content.ReadAsByteArrayAsync(cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsByteArrayAsync();
#endif
    }
}
