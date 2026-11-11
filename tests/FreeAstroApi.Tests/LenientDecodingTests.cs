using System.Text.Json;
using FreeAstroApi.Models;
using FreeAstroApi.Serialization;

namespace FreeAstroApi.Tests;

/// <summary>Response schemas are partly inferred from samples, so decoding must never throw on drift.</summary>
public class LenientDecodingTests
{
    private static NatalChartRequest Birth() => new() { Year = 1990, Month = 6, Day = 15, City = "Paris" };

    [Fact]
    public async Task MissingRequiredNullNonNullableUnknownFieldAndUnknownEnumValueDoNotThrow()
    {
        var (client, handler, _) = TestClients.Create();
        // `sign` (required) is missing, `degree`/`house` (required numbers) are null, `abs_pos` is a numeric
        // string, `sign_id` is outside its enum, and `constellation` is not in the schema.
        handler.Respond(HttpStatusCode.OK, """
            {"point": "Sun", "sign_id": "ophiuchus", "degree": null, "house": null, "abs_pos": "84.5",
             "interpretation": null, "constellation": {"name": "Ophiuchus", "stars": [1, null]}}
            """);

        var sun = await client.Western.Signs.SunAsync(Birth());

        Assert.Equal("Sun", sun.Point);
        Assert.Null(sun.Sign);
        Assert.Equal("ophiuchus", sun.SignId);
        Assert.Equal(0, sun.Degree);
        Assert.Equal(0, sun.House);
        Assert.Equal(84.5, sun.AbsPos);
        Assert.NotNull(sun.AdditionalProperties);
        Assert.Equal("Ophiuchus", sun.AdditionalProperties!["constellation"].GetProperty("name").GetString());
    }

    [Fact]
    public void NullsInValueTypeCollectionsAndBooleansReadAsDefaults()
    {
        var numbers = JsonSerializer.Deserialize<List<double>>("[1.5, null, \"2\"]", FreeAstroJson.Options)!;
        Assert.Equal([1.5, 0, 2], numbers);

        var map = JsonSerializer.Deserialize<Dictionary<string, int>>("""{"a": null, "b": 3}""", FreeAstroJson.Options)!;
        Assert.Equal(0, map["a"]);
        Assert.Equal(3, map["b"]);

        Assert.False(JsonSerializer.Deserialize<bool>("null", FreeAstroJson.Options));
        Assert.Equal(0L, JsonSerializer.Deserialize<long>("null", FreeAstroJson.Options));
        Assert.Null(JsonSerializer.Deserialize<int?>("null", FreeAstroJson.Options));
    }

    [Fact]
    public void SerializationIsUnchanged()
    {
        var json = JsonSerializer.Serialize(Birth(), FreeAstroJson.Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1990, document.RootElement.GetProperty("year").GetInt32());
        Assert.Equal("Paris", document.RootElement.GetProperty("city").GetString());
    }
}
