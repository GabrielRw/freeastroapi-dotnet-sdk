using System.IO;
using System.Text;

namespace FreeAstroApi.Tests;

/// <summary>A captured request (the body is read before the message is disposed).</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body, string? ContentType);

/// <summary>An HttpMessageHandler that answers from a script and records every request.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public FakeHandler Respond(HttpStatusCode status, string body = "{}", string contentType = "application/json", Action<HttpResponseMessage>? configure = null) =>
        Respond(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
            configure?.Invoke(response);
            return response;
        });

    public FakeHandler Throw(Exception exception) => Respond(_ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // User-Agent product tokens are space-separated on the wire; other repeated values are comma-separated.
        var headers = request.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ", ", h.Value),
            StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.ToString()));
        if (_responses.Count == 0) throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}");
        var response = _responses.Dequeue()(request);
        response.RequestMessage = request;
        return response;
    }
}

public static class TestClients
{
    public const string ApiKey = "test-key-123";

    public static (FreeAstroClient Client, FakeHandler Handler, List<TimeSpan> Delays) Create(Action<FreeAstroClientOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var options = new FreeAstroClientOptions { ApiKey = ApiKey, BaseUrl = new Uri("https://api.test.example") };
        configure?.Invoke(options);
        var client = new FreeAstroClient(new HttpClient(handler), options);
        var delays = new List<TimeSpan>();
        client.Transport.Delay = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };
        return (client, handler, delays);
    }
}

public static class RepoPaths
{
    public static string Root { get; } = Find();

    public static string Spec => Path.Combine(Root, "spec", "freeastro.json");

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreeAstroApi.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("repository root not found");
    }
}
