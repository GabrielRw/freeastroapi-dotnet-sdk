using System.Net.Http.Headers;
using FreeAstroApi.Models;

namespace FreeAstroApi.Tests;

public class RetryTests
{
    private static readonly GeoSearchParams Paris = new() { Q = "Paris" };
    private const string Cities = """{"results": [], "count": 0}""";

    private static NatalChartRequest Birth() => new() { Year = 1990, Month = 6, Day = 15, City = "Paris" };

    [Fact]
    public async Task GetRetriesServerErrorsWithExponentialBackoff()
    {
        var (client, handler, delays) = TestClients.Create();
        handler.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.OK, Cities);

        var result = await client.Geo.SearchAsync(Paris);

        Assert.Equal(0, result.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task RetryAfterSecondsAndDatesAreHonoured()
    {
        var (client, handler, delays) = TestClients.Create();
        handler
            .Respond(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)))
            .Respond(HttpStatusCode.ServiceUnavailable, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(20)))
            .Respond(HttpStatusCode.OK, Cities);

        await client.Geo.SearchAsync(Paris);

        Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
        Assert.InRange(delays[1], TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task RateLimitedPostIsRetried()
    {
        var (client, handler, delays) = TestClients.Create();
        handler.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.OK, "{}");

        await client.Natal.CalculateAsync(Birth());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Single(delays);
    }

    [Fact]
    public async Task PostIsNotRetriedOnServerErrorsWithoutAnIdempotencyKey()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.InternalServerError, """{"detail": "boom"}""");

        await Assert.ThrowsAsync<FreeAstroServerException>(() => client.Natal.CalculateAsync(Birth()));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostWithIdempotencyKeyIsRetried()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.OK, "{}");

        await client.Natal.CalculateAsync(Birth(), options: new RequestOptions { IdempotencyKey = "key-1" });

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("key-1", r.Headers["Idempotency-Key"]));
    }

    [Fact]
    public async Task PostCanOptIntoRetries()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, "{}");

        await client.Natal.CalculateAsync(Birth(), options: new RequestOptions { Retry = true });

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterMaxRetries()
    {
        var (client, handler, delays) = TestClients.Create(o => o.MaxRetries = 2);
        for (var i = 0; i < 3; i++) handler.Respond(HttpStatusCode.ServiceUnavailable, """{"detail": "down"}""");

        var error = await Assert.ThrowsAsync<FreeAstroServerException>(() => client.Geo.SearchAsync(Paris));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task RetriesCanBeDisabledPerRequest()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<FreeAstroRateLimitException>(() => client.Geo.SearchAsync(Paris, new RequestOptions { Retry = false }));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryAfterLongerThanTheCapIsSurfacedInsteadOfWaited()
    {
        var (client, handler, delays) = TestClients.Create(o => o.MaxRetryDelay = TimeSpan.FromSeconds(10));
        handler.Respond(HttpStatusCode.TooManyRequests, """{"detail": "slow down"}""", configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)));

        var error = await Assert.ThrowsAsync<FreeAstroRateLimitException>(() => client.Geo.SearchAsync(Paris));

        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
        Assert.Empty(delays);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NetworkFailuresAreRetriedForGetThenWrapped()
    {
        var (client, handler, _) = TestClients.Create(o => o.MaxRetries = 1);
        handler.Throw(new HttpRequestException("connection refused")).Throw(new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<FreeAstroNetworkException>(() => client.Geo.SearchAsync(Paris));

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutsRaiseFreeAstroTimeoutException()
    {
        var handler = new SlowHandler();
        using var client = new FreeAstroClient(new HttpClient(handler), new FreeAstroClientOptions
        {
            ApiKey = "k",
            Timeout = TimeSpan.FromMilliseconds(50),
            MaxRetries = 0,
        });

        await Assert.ThrowsAsync<FreeAstroTimeoutException>(() => client.Geo.SearchAsync(Paris));
    }

    [Fact]
    public async Task CallerCancellationIsNotWrapped()
    {
        var handler = new SlowHandler();
        using var client = new FreeAstroClient(new HttpClient(handler), new FreeAstroClientOptions { ApiKey = "k" });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Geo.SearchAsync(Paris, cancellationToken: cts.Token));

        Assert.IsNotAssignableFrom<FreeAstroException>(error);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
