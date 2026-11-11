using FreeAstroApi.Models;

namespace FreeAstroApi.Tests.Live;

/// <summary>A fact that only runs when <c>FREEASTRO_TEST_API_KEY</c> is set.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FREEASTRO_TEST_API_KEY")))
        {
            Skip = "Set FREEASTRO_TEST_API_KEY to call the production API.";
        }
    }
}

/// <summary>
/// Nightly smoke test against production (.github/workflows/live-smoke.yml): five cheap read-only calls,
/// with retries off so a failure never spends extra quota.
/// <code>FREEASTRO_TEST_API_KEY=... dotnet test --filter Category=Live</code>
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveSmokeTests : IDisposable
{
    private readonly FreeAstroClient _client = new(new FreeAstroClientOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("FREEASTRO_TEST_API_KEY") ?? "unset",
        BaseUrl = new Uri(Environment.GetEnvironmentVariable("FREEASTRO_BASE_URL") ?? FreeAstroClientOptions.DefaultBaseUrl),
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(60),
    });

    public void Dispose() => _client.Dispose();

    [LiveFact]
    public async Task GeoSearch()
    {
        var cities = await _client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris", Limit = 1 });

        Assert.NotEmpty(cities.Results);
        Assert.False(string.IsNullOrEmpty(cities.Results[0].Timezone));
    }

    [LiveFact]
    public async Task MoonPhase()
    {
        var phase = await _client.Moon.PhaseAsync(new MoonPhaseParams { Date = "2026-01-15" });

        Assert.False(string.IsNullOrEmpty(phase.Phase.Name));
    }

    [LiveFact]
    public async Task WesternSunSign()
    {
        var sign = await _client.Western.Signs.SunAsync(new NatalChartRequest { Year = 1990, Month = 6, Day = 15, City = "Paris" });

        Assert.Equal("gemini", sign.SignId);
    }

    [LiveFact]
    public async Task NatalCalculate()
    {
        var chart = await _client.Natal.CalculateAsync(new NatalChartRequest { Year = 1990, Month = 6, Day = 15, Hour = 14, Minute = 30, City = "Paris" });

        Assert.Equal(System.Text.Json.JsonValueKind.Object, chart.ValueKind);
    }

    [LiveFact]
    public async Task VedicPanchang()
    {
        var panchang = await _client.Vedic.PanchangAsync(new PanchangRequestV2 { Year = 2026, Month = 1, Day = 14, Hour = 6, Minute = 0, City = "Delhi" });

        Assert.False(string.IsNullOrEmpty(panchang.Sunrise));
    }
}
