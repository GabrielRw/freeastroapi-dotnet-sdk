using FreeAstroApi.Models;

namespace FreeAstroApi.Tests;

public class ErrorTests
{
    [Theory]
    [InlineData(400, typeof(FreeAstroValidationException))]
    [InlineData(401, typeof(FreeAstroAuthenticationException))]
    [InlineData(403, typeof(FreeAstroPermissionException))]
    [InlineData(404, typeof(FreeAstroNotFoundException))]
    [InlineData(409, typeof(FreeAstroConflictException))]
    [InlineData(418, typeof(FreeAstroApiException))]
    [InlineData(422, typeof(FreeAstroValidationException))]
    [InlineData(429, typeof(FreeAstroRateLimitException))]
    [InlineData(500, typeof(FreeAstroServerException))]
    [InlineData(503, typeof(FreeAstroServerException))]
    public async Task StatusCodesMapToExceptionTypes(int status, Type expected)
    {
        var (client, handler, _) = TestClients.Create(o => o.MaxRetries = 0);
        handler.Respond((HttpStatusCode)status, """{"detail": "nope", "code": "E_TEST"}""", configure: r => r.Headers.Add("x-request-id", "req-42"));

        var error = await Assert.ThrowsAsync(expected, () => client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" }));

        var api = Assert.IsAssignableFrom<FreeAstroApiException>(error);
        Assert.Equal(status, api.StatusCode);
        Assert.Equal("req-42", api.RequestId);
        Assert.Equal("E_TEST", api.ErrorCode);
        Assert.Equal("""{"detail": "nope", "code": "E_TEST"}""", api.Body);
        Assert.Contains("nope", api.Message, StringComparison.Ordinal);
        Assert.Equal("nope", api.Error!.Value.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ValidationDetailsAreSummarised()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.UnprocessableEntity, """
            {"detail": [{"loc": ["body", "year"], "msg": "Field required", "type": "missing"},
                        {"loc": ["body", "month"], "msg": "Input should be less than 13", "type": "less_than"}]}
            """);

        var error = await Assert.ThrowsAsync<FreeAstroValidationException>(
            () => client.Natal.CalculateAsync(new NatalChartRequest { City = "Paris" }));

        Assert.Contains("body.year: Field required; body.month: Input should be less than 13", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceIdAndErrorCodeFallbacksAndProblemJson()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.Conflict, """{"error": "idempotency_key_reused", "detail": "This key was used.", "trace_id": "t-1"}""", "application/problem+json");

        var error = await Assert.ThrowsAsync<FreeAstroConflictException>(
            () => client.Natal.CalculateAsync(new NatalChartRequest { City = "Paris" }));

        Assert.Equal("t-1", error.RequestId);
        Assert.Equal("idempotency_key_reused", error.ErrorCode);
        Assert.Contains("This key was used.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonJsonErrorBodiesAreKeptRaw()
    {
        var (client, handler, _) = TestClients.Create(o => o.MaxRetries = 0);
        handler.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var error = await Assert.ThrowsAsync<FreeAstroServerException>(() => client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" }));

        Assert.Equal("<html>bad gateway</html>", error.Body);
        Assert.Null(error.Error);
        Assert.Null(error.ErrorCode);
    }
}
