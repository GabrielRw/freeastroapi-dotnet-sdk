using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FreeAstroApi.Internal;

namespace FreeAstroApi;

/// <summary>
/// The FreeAstroAPI client. Resources follow the API paths: <c>client.Western.Natal.InsightsAsync(...)</c>
/// calls <c>POST /api/v1/western/natal/insights</c>. When an operation exists in several API versions the
/// plain method name calls the latest one and older ones carry a suffix (<c>client.Vedic.ChartAsync</c> is
/// v2, <c>client.Vedic.ChartV1Async</c> is v1).
/// </summary>
/// <remarks>
/// The client is thread-safe; create one and reuse it. When it creates its own <see cref="HttpClient"/> it
/// disposes it in <see cref="Dispose"/>; an injected <see cref="HttpClient"/> is never disposed.
/// </remarks>
public sealed partial class FreeAstroClient : IDisposable
{
    /// <summary>The environment variable read when <see cref="FreeAstroClientOptions.ApiKey"/> is not set.</summary>
    public const string ApiKeyEnvironmentVariable = "FREEASTRO_API_KEY";

    /// <summary>The name of the <see cref="IHttpClientFactory"/> client registered by <c>AddFreeAstro()</c>.</summary>
    public const string HttpClientName = "FreeAstroApi";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly FreeAstroTransport _transport;

    /// <summary>Creates a client configured from the <c>FREEASTRO_API_KEY</c> environment variable.</summary>
    public FreeAstroClient()
        : this(new FreeAstroClientOptions())
    {
    }

    /// <summary>Creates a client with an API key.</summary>
    /// <param name="apiKey">Your FreeAstroAPI key.</param>
    public FreeAstroClient(string apiKey)
        : this(new FreeAstroClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a client that owns its own <see cref="HttpClient"/>.</summary>
    /// <param name="options">The client options.</param>
    public FreeAstroClient(FreeAstroClientOptions options)
        : this(null, options, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a client on top of an existing <see cref="HttpClient"/>, for example one from
    /// <see cref="IHttpClientFactory"/>. The client does not dispose it.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send requests with.</param>
    /// <param name="options">The client options; <see langword="null"/> uses the defaults and the environment API key.</param>
    public FreeAstroClient(HttpClient httpClient, FreeAstroClientOptions? options = null)
        : this(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), options ?? new FreeAstroClientOptions(), ownsHttpClient: false)
    {
    }

    private FreeAstroClient(HttpClient? httpClient, FreeAstroClientOptions options, bool ownsHttpClient)
    {
        FreeAstroApi.Internal.Guard.NotNull(options, nameof(options));
        Options = options.Snapshot();
        var apiKey = string.IsNullOrWhiteSpace(Options.ApiKey) ? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) : Options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                $"A FreeAstroAPI key is required: set FreeAstroClientOptions.ApiKey or the {ApiKeyEnvironmentVariable} environment variable.",
                nameof(options));
        }
        if (Options.BaseUrl is null || !Options.BaseUrl.IsAbsoluteUri) throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(options));
        if (Options.Timeout <= TimeSpan.Zero && Options.Timeout != Timeout.InfiniteTimeSpan) throw new ArgumentException("Timeout must be positive.", nameof(options));

        _ownsHttpClient = ownsHttpClient;
        _http = httpClient ?? CreateHttpClient();
        _transport = new FreeAstroTransport(_http, Options, apiKey!.Trim());
        InitializeResources(_transport);
    }

    /// <summary>The SDK version, for example <c>0.1.0</c>.</summary>
    public static string SdkVersion { get; } = ReadVersion();

    /// <summary>The <c>User-Agent</c> sent with every request: <c>freeastroapi-dotnet/&lt;version&gt;</c>.</summary>
    public static string UserAgent { get; } = "freeastroapi-dotnet/" + SdkVersion;

    /// <summary>A snapshot of the options this client was created with (with the API key as configured).</summary>
    internal FreeAstroClientOptions Options { get; }

    internal FreeAstroTransport Transport => _transport;

    /// <summary>
    /// Calls any endpoint, for operations newer than this SDK version. The body is serialized as JSON and the
    /// JSON response is deserialized into <typeparamref name="T"/> (use <see cref="System.Text.Json.JsonElement"/>
    /// for untyped access).
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path, for example <c>/api/v1/natal/calculate</c>.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="query">Query parameters; <see langword="null"/> values are skipped.</param>
    /// <param name="options">Per-request options.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The deserialized response.</returns>
    public Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        IEnumerable<KeyValuePair<string, object?>>? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _transport.SendJsonAsync<T>(BuildRequest(method, path, body, query, "application/json"), options, cancellationToken);
    }

    /// <summary>Calls any endpoint and returns the raw response body (images, PDF, Markdown or JSON).</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path, for example <c>/api/v2/vedic/kp/render</c>.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="query">Query parameters; <see langword="null"/> values are skipped.</param>
    /// <param name="accept">The <c>Accept</c> header, for example <c>image/png</c>.</param>
    /// <param name="options">Per-request options.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response body and its content type.</returns>
    public Task<FreeAstroContent> SendRawAsync(
        HttpMethod method,
        string path,
        object? body = null,
        IEnumerable<KeyValuePair<string, object?>>? query = null,
        string accept = "*/*",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _transport.SendContentAsync(BuildRequest(method, path, body, query, accept), options, cancellationToken);
    }

    /// <summary>Disposes the <see cref="HttpClient"/> if this client created it.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    private static FreeAstroRequest BuildRequest(HttpMethod method, string path, object? body, IEnumerable<KeyValuePair<string, object?>>? query, string accept)
    {
        FreeAstroApi.Internal.Guard.NotNull(method, nameof(method));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", nameof(path));
        var request = new FreeAstroRequest(method.Method.ToUpperInvariant(), path[0] == '/' ? path : "/" + path, accept);
        if (body is not null) request.SetJsonBody(body);
        foreach (var pair in query ?? Enumerable.Empty<KeyValuePair<string, object?>>()) request.Query.Add(pair.Key, pair.Value);
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
#if NET
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };
#else
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
#endif
        // Per-attempt timeouts are enforced by the transport.
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static string ReadVersion()
    {
        var assembly = typeof(FreeAstroClient).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational!.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }
        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
