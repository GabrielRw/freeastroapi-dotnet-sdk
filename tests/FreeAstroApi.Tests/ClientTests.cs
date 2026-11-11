using FreeAstroApi.Models;

namespace FreeAstroApi.Tests;

public class ClientTests
{
    [Fact]
    public async Task SendsApiKeyUserAgentAndAcceptHeaders()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, """{"results": [], "count": 0}""");

        await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(TestClients.ApiKey, request.Headers["x-api-key"]);
        Assert.Equal("freeastroapi-dotnet/" + FreeAstroClient.SdkVersion, request.Headers["User-Agent"]);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.Equal("0.1.0", FreeAstroClient.SdkVersion);
    }

    [Fact]
    public async Task DefaultAndPerRequestHeadersAreSentButCannotReplaceTheApiKey()
    {
        var (client, handler, _) = TestClients.Create(o =>
        {
            o.DefaultHeaders["X-Tenant"] = "acme";
            o.DefaultHeaders["User-Agent"] = "my-app/2.0";
        });
        handler.Respond(HttpStatusCode.OK, """{"results": [], "count": 0}""");
        var options = new RequestOptions();
        options.Headers["X-Trace"] = "abc";
        options.Headers["x-api-key"] = "spoofed";

        await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" }, options);

        var headers = handler.Requests[0].Headers;
        Assert.Equal("acme", headers["X-Tenant"]);
        Assert.Equal("abc", headers["X-Trace"]);
        Assert.Equal(TestClients.ApiKey, headers["x-api-key"]);
        Assert.Equal("my-app/2.0 " + FreeAstroClient.UserAgent, headers["User-Agent"]);
    }

    [Fact]
    public async Task BaseUrlPathPrefixIsKept()
    {
        var (client, handler, _) = TestClients.Create(o => o.BaseUrl = new Uri("https://proxy.example/freeastro/"));
        handler.Respond(HttpStatusCode.OK, """{"results": [], "count": 0}""");

        await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" });

        Assert.Equal("https://proxy.example/freeastro/api/v2/geo/search?q=Paris", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public void InjectedHttpClientIsNotDisposed()
    {
        var handler = new FakeHandler().Respond(HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var client = new FreeAstroClient(http, new FreeAstroClientOptions { ApiKey = "k" });

        client.Dispose();

        Assert.Equal(new Uri("https://api.freeastroapi.com/"), new FreeAstroClientOptions().BaseUrl);
        _ = http.Timeout; // still usable
        http.Dispose();
    }

    [Fact]
    public void OptionsAreSnapshotAtConstruction()
    {
        var options = new FreeAstroClientOptions { ApiKey = "k", MaxRetries = 1 };
        using var client = new FreeAstroClient(new HttpClient(new FakeHandler()), options);

        options.MaxRetries = 9;

        Assert.Equal(1, client.Options.MaxRetries);
    }

    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentException>(() => new FreeAstroClient(new FreeAstroClientOptions { ApiKey = "k", BaseUrl = new Uri("/relative", UriKind.Relative) }));
        Assert.Throws<ArgumentException>(() => new FreeAstroClient(new FreeAstroClientOptions { ApiKey = "k", Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentNullException>(() => new FreeAstroClient((HttpClient)null!));
    }
}

/// <summary>Tests that change process environment variables; they must not run in parallel.</summary>
[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection
{
}

[Collection(nameof(EnvironmentCollection))]
public class ApiKeyEnvironmentTests
{
    [Fact]
    public async Task FallsBackToTheEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable, "env-key");
        try
        {
            var handler = new FakeHandler().Respond(HttpStatusCode.OK, """{"results": [], "count": 0}""");
            using var client = new FreeAstroClient(new HttpClient(handler));

            await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" });

            Assert.Equal("env-key", handler.Requests[0].Headers["x-api-key"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ThrowsWithoutAnApiKey()
    {
        var previous = Environment.GetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable, null);
        try
        {
            var error = Assert.Throws<ArgumentException>(() => new FreeAstroClient());
            Assert.Contains("FREEASTRO_API_KEY", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FreeAstroClient.ApiKeyEnvironmentVariable, previous);
        }
    }
}
