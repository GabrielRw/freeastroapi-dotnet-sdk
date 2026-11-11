using System.Text.Json;
using FreeAstroApi.Internal;
using FreeAstroApi.Models;
using FreeAstroApi.Serialization;

namespace FreeAstroApi.Tests;

public class RequestTests
{
    [Fact]
    public async Task BodyCallSerializesSnakeCaseAndOmitsNulls()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, """{"planets": [{"id": "sun"}]}""");

        var chart = await client.Natal.CalculateAsync(new NatalChartRequest
        {
            Year = 1990, Month = 6, Day = 15, Hour = 14, Minute = 30, City = "Paris", TzStr = "Europe/Paris",
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test.example/api/v1/natal/calculate", request.Uri.AbsoluteUri);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(1990, body.RootElement.GetProperty("year").GetInt32());
        Assert.Equal("Europe/Paris", body.RootElement.GetProperty("tz_str").GetString());
        Assert.False(body.RootElement.TryGetProperty("lat", out _));
        Assert.False(body.RootElement.TryGetProperty("name", out _));
        Assert.Equal("sun", chart.Planets![0].Id);
    }

    [Fact]
    public async Task QueryCallDeserializesTypedResponseAndKeepsUnknownFields()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, """
            {"results": [{"name": "Paris", "country": "FR", "lat": 48.85, "lng": 2.35, "timezone": "Europe/Paris",
              "population": 2100000, "new_field": {"x": 1}}], "count": 1}
            """);

        var cities = await client.Geo.SearchAsync(new GeoSearchParams { Q = "São Paulo & co", Limit = 3 });

        Assert.Equal("https://api.test.example/api/v2/geo/search?q=S%C3%A3o%20Paulo%20%26%20co&limit=3", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Null(handler.Requests[0].Body);
        var city = Assert.Single(cities.Results);
        Assert.Equal("Europe/Paris", city.Timezone);
        Assert.Equal(48.85, city.Lat);
        Assert.Equal(1, city.AdditionalProperties!["new_field"].GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task PathParametersAreEscaped()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, "{}");

        await client.Chinese.CalendarAsync("2026/02 17", new ChineseCalendarParams { Lang = "en" });

        Assert.Equal("/api/v1/chinese/calendar/2026%2F02%2017?lang=en", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task RequiredArgumentsAreValidated()
    {
        var (client, _, _) = TestClients.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Natal.CalculateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Geo.SearchAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Geo.SearchAsync(new GeoSearchParams()));
    }

    [Fact]
    public void QueryValuesUseInvariantFormsAndExplodeLists()
    {
        var query = new QueryBuilder();
        query.Add("flag", true);
        query.Add("off", false);
        query.Add("lat", 48.8566);
        query.Add("n", 3);
        query.Add("skip", null);
        query.Add("bodies", new List<string> { "sun", "moon" });

        Assert.Equal("?flag=true&off=false&lat=48.8566&n=3&bodies=sun&bodies=moon", query.ToString());
    }

    [Fact]
    public async Task EscapeHatchCallsAnyEndpoint()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, """{"ok": true}""");

        var result = await client.SendAsync<JsonElement>(
            HttpMethod.Post,
            "api/v9/future/endpoint",
            new Dictionary<string, object?> { ["year"] = 2030 },
            new Dictionary<string, object?> { ["lang"] = "fr", ["missing"] = null });

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("/api/v9/future/endpoint?lang=fr", handler.Requests[0].Uri.PathAndQuery);
        Assert.Equal("""{"year":2030}""", handler.Requests[0].Body);
    }

    [Fact]
    public void JsonOptionsDeserializeModelsFromElements()
    {
        using var document = JsonDocument.Parse("""{"name": "Lyon", "country": "FR", "lat": 45.7, "lng": 4.8, "timezone": "Europe/Paris", "population": 1}""");

        var city = document.RootElement.Deserialize<CityResult>(FreeAstroJson.Options);

        Assert.Equal("Lyon", city!.Name);
    }

    [Fact]
    public async Task MalformedSuccessBodyRaisesDeserializationException()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, """{"results": "not-a-list"}""");

        var error = await Assert.ThrowsAsync<FreeAstroDeserializationException>(() => client.Geo.SearchAsync(new GeoSearchParams { Q = "x" }));

        Assert.Contains("not-a-list", error.Body, StringComparison.Ordinal);
    }
}
