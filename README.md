# FreeAstroApi.Sdk

[![CI](https://github.com/GabrielRw/freeastroapi-dotnet-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/GabrielRw/freeastroapi-dotnet-sdk/actions/workflows/ci.yml)

Official .NET client for [FreeAstroAPI](https://www.freeastroapi.com): Western natal charts, transits, progressions, returns, synastry and astrocartography; Vedic charts, dashas, panchang, compatibility and muhurat; Chinese BaZi; numerology; horoscopes; Moon phases, sky events and ephemerides.

Targets `net10.0`, `net8.0` and `netstandard2.0` (.NET Framework 4.6.2+, Unity, Xamarin). Every one of the 142 public operations has a typed async method, and every JSON response is a typed model except five progression endpoints whose shape depends on the request.

## Install

```sh
dotnet add package FreeAstroApi.Sdk
```

## Quick start

```csharp
using FreeAstroApi;
using FreeAstroApi.Models;

using var client = new FreeAstroClient(new FreeAstroClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("FREEASTRO_API_KEY"),
});

var cities = await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris", Limit = 3 });
Console.WriteLine(cities.Results[0].Timezone); // Europe/Paris
```

`new FreeAstroClient()` reads the key from the `FREEASTRO_API_KEY` environment variable. Keep API keys on the server; never ship them in a desktop, mobile or Blazor WebAssembly app.

Options: `ApiKey`, `BaseUrl` (default `https://api.freeastroapi.com`), `Timeout` (per attempt, default 30 s), `MaxRetries` (default 2), `InitialRetryDelay`, `MaxBackoff`, `MaxRetryDelay` and `DefaultHeaders`. The client is thread-safe: create one and reuse it.

## Examples

### Natal chart

```csharp
// POST /api/v1/natal/calculate
var chart = await client.Natal.CalculateAsync(new NatalChartRequest
{
    Year = 1990, Month = 6, Day = 15, Hour = 14, Minute = 30,
    City = "Paris",
});
// -> typed NatalCalculateResponse
Console.WriteLine(chart.Planets![0].Name);

// POST /api/v1/western/signs/sun -> typed SignResponse
var sun = await client.Western.Signs.SunAsync(new NatalChartRequest { Year = 1990, Month = 6, Day = 15, City = "Paris" });
Console.WriteLine(sun.Sign);
```

### Vedic panchang

```csharp
// POST /api/v2/vedic/panchang -> PanchangV2Response
var panchang = await client.Vedic.PanchangAsync(new PanchangRequestV2
{
    Year = 2026, Month = 1, Day = 14, Hour = 6, Minute = 0,
    City = "Delhi",
});
Console.WriteLine($"Sunrise {panchang.Sunrise}, weekday {panchang.Weekday.Name}");
```

### Geo search

```csharp
// GET /api/v2/geo/search?q=Kyoto&limit=5
var result = await client.Geo.SearchAsync(new GeoSearchParams { Q = "Kyoto", Limit = 5 });
foreach (var city in result.Results)
{
    Console.WriteLine($"{city.Name}, {city.Country} ({city.Lat}, {city.Lng}) {city.Timezone}");
}
```

### Rendered charts (SVG / PNG)

```csharp
// POST /api/v2/vedic/kp/render returns image/svg+xml or image/png
var image = await client.Vedic.KpRenderAsync(new KPChartRenderRequest { Year = 1990, Month = 6, Day = 15, Hour = 14, Minute = 30, City = "Delhi" });
Console.WriteLine(image.ContentType);                 // image/svg+xml
await File.WriteAllBytesAsync("chart.svg", image.ToArray());
string svg = image.ReadAsString();
```

Operations that return text only (Markdown, SVG) return `string`; binary or mixed bodies return `FreeAstroContent` (`Data`, `ContentType`, `FileName`, `ToArray()`, `OpenRead()`, `ReadAsString()`).

### ASP.NET Core and dependency injection

```csharp
// Program.cs
builder.Services.AddFreeAstro(options =>
{
    options.ApiKey = builder.Configuration["FreeAstro:ApiKey"];
});

// or bind the whole section: { "FreeAstro": { "ApiKey": "...", "Timeout": "00:00:20" } }
builder.Services.Configure<FreeAstroClientOptions>(builder.Configuration.GetSection("FreeAstro"));
builder.Services.AddFreeAstro();

app.MapGet("/sun-sign", async (FreeAstroClient astro, int year, int month, int day, string city) =>
    (await astro.Western.Signs.SunAsync(new NatalChartRequest { Year = year, Month = month, Day = day, City = city })).Sign);
```

`AddFreeAstro()` registers `FreeAstroClient` as a transient service on a named `IHttpClientFactory` client (`FreeAstroClient.HttpClientName`) and returns the `IHttpClientBuilder`, so you can add handlers such as logging or `AddStandardResilienceHandler()`. You can also pass any `HttpClient` yourself: `new FreeAstroClient(httpClient, options)` (the client never disposes an injected `HttpClient`).

## Resources and naming

Resources follow the API paths:

| Call | Endpoint |
| --- | --- |
| `client.Natal.CalculateAsync(body)` | `POST /api/v1/natal/calculate` |
| `client.Western.Natal.InsightsAsync(body)` | `POST /api/v1/western/natal/insights` |
| `client.Western.Progressions.SecondaryExactAspectsAsync(body)` | `POST /api/v1/western/progressions/secondary/exact-aspects` |
| `client.Chinese.CalendarAsync("2026-02-17", new ChineseCalendarParams { Lang = "en" })` | `GET /api/v1/chinese/calendar/{date}` |
| `client.Horoscope.Daily.SignAsync(new HoroscopeDailySignParams { Sign = "aries" })` | `GET /api/v3/horoscope/daily/sign` |

Method arguments are `(pathParams..., body, parameters, options, cancellationToken)`: path parameters are positional, the JSON body is a generated model, and query parameters are a generated `*Params` class.

When an operation exists in several API versions, the plain method name calls the latest version and older ones carry a suffix: `client.Vedic.ChartAsync` calls `/api/v2/vedic/chart`, `client.Vedic.ChartV1Async` calls `/api/v1/vedic/chart`, and `client.Horoscope.Daily.SignV1Async` / `SignV2Async` reach the older daily horoscopes. Deprecated operations are marked `[Obsolete]`. This is the same rule as the TypeScript and Python SDKs.

Resource groups: `Ai`, `Chinese`, `Ephemeris`, `Geo`, `Horoscope`, `Moon`, `Natal`, `Numerology`, `SkyEvents`, `Space`, `Transits`, `Vedic` and `Western` (with `Astrocartography`, `Directions`, `Electional`, `Natal`, `Progressions`, `Signs`, `Transits` and more). `FreeAstroOperations.All` lists every operation and its client method.

### Models

Request and response models in `FreeAstroApi.Models` are generated from the spec's component schemas. Property names are PascalCase and map to the API's snake_case fields; `null` properties are not sent, so the API applies its defaults. Fields the API returns that this SDK version does not know yet land in each model's `AdditionalProperties`.

Response decoding is lenient, so API changes do not break your build or your calls: a missing field keeps its default, `null` in a non-nullable number or boolean reads as `0`/`false`, unknown fields go to `AdditionalProperties`, and enum-like fields are plain strings, so new values come through as-is. Request models are not affected.

The five progression endpoints (`client.Western.Progressions.SecondaryAsync` and friends) return either a chart or a table depending on the request, so they return `System.Text.Json.JsonElement`; the XML docs name both models. Read it with the SDK's serializer settings:

```csharp
using FreeAstroApi.Serialization;
var typed = element.Deserialize<SecondaryProgressionsResponse>(FreeAstroJson.Options);
```

### Other endpoints

Call anything newer than this SDK version:

```csharp
var result = await client.SendAsync<JsonElement>(HttpMethod.Post, "/api/v1/example", new { year = 1990 });
var file = await client.SendRawAsync(HttpMethod.Get, "/api/v1/example.pdf", accept: "application/pdf");
```

## Retries, timeouts and idempotency

Failed requests are retried up to `MaxRetries` times (default 2):

- **HTTP 429** is retried for every operation (the request was rejected before it ran).
- **HTTP 408, 500, 502, 503, 504, timeouts and network failures** are retried for every operation except the side-effecting ones in `FreeAstroOperations.SideEffectingOperationIds`: `v1_natal_calculate` (may create a paid report job), `v1_ai_generate`, `v2_vedic_chat`, `v2_western_chat`, `v2_vedic_qa`, `v1_western_transits_timeline` and `v1_western_transits_insights`. Those are retried only when the request carries an `IdempotencyKey` (sent as the `Idempotency-Key` header) or opts in with `Retry = true`.
- **Other 4xx** are never retried.
- **Backoff** is exponential with full jitter: retry *n* waits a random time between 0 and `InitialRetryDelay * 2^n` (500 ms, 1 s, 2 s, ...), capped at `MaxBackoff` (8 s).
- **Server-requested waits** win over the backoff: the `Retry-After` header (seconds or HTTP date) or `retry_after_ms` in the error body. If the wait is longer than `MaxRetryDelay` (30 s) the client does not wait; it throws the error at once with `RetryAfter` set (`FreeAstroRateLimitException` for a 429).
- `SendAsync` / `SendRawAsync` follow the policy of the operation with that method and path; an unknown non-GET endpoint is treated as side-effecting.

```csharp
await client.Vedic.ChatAsync(question, new RequestOptions
{
    IdempotencyKey = "stable-key-for-this-question",
    Timeout = TimeSpan.FromSeconds(90),
});
```

Per request, `RequestOptions` overrides `MaxRetries`, sets the `IdempotencyKey`, opts in (`Retry = true`) or disables retries (`Retry = false`). Cancelling the `CancellationToken` throws `OperationCanceledException`, not an SDK exception.

## Errors

| Exception | When |
| --- | --- |
| `FreeAstroValidationException` | 400, 422 (FastAPI validation details are summarised in `Message`) |
| `FreeAstroAuthenticationException` | 401 |
| `FreeAstroPermissionException` | 403 |
| `FreeAstroNotFoundException` | 404 |
| `FreeAstroConflictException` | 409 |
| `FreeAstroRateLimitException` | 429 (`RetryAfter` from `Retry-After` or `retry_after_ms`) |
| `FreeAstroServerException` | 5xx |
| `FreeAstroApiException` | any other status; base class of the above, with `StatusCode`, `Body`, `RequestId`, `ErrorCode` (`error`/`code`, or `detail.error` for route errors), `RetryAfter` and `Error` (the parsed `ErrorResponse` JSON) |
| `FreeAstroNetworkException` / `FreeAstroTimeoutException` | the API could not be reached / timed out |
| `FreeAstroDeserializationException` | a success body did not match the expected model |

All derive from `FreeAstroException`.

```csharp
try
{
    await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" });
}
catch (FreeAstroRateLimitException ex)
{
    Console.WriteLine($"Slow down, retry after {ex.RetryAfter}");
}
catch (FreeAstroApiException ex)
{
    Console.WriteLine($"{ex.StatusCode} {ex.ErrorCode}: {ex.Message} (request {ex.RequestId})");
}
```

## Development

The generated code in `src/FreeAstroApi/Generated/` comes from the vendored `spec/freeastro.json` via the in-repo generator in `tools/FreeAstroApi.Generator` (C#, no third-party dependencies). It reads the OpenAPI 3.1 document directly, so no preprocessing step is needed. `scripts/spec_sync.py` and `scripts/with_prism.py` are shared, unmodified, with the TypeScript and Python SDKs, so every SDK vendors byte-identical spec files.

```sh
scripts/check.sh                 # generated code is current, build, tests, fixture build, pack
scripts/generate.sh              # regenerate from spec/freeastro.json
scripts/sync-spec.sh             # fetch the production spec, normalise it, regenerate
python3 scripts/with_prism.py -- dotnet test tests/FreeAstroApi.Tests --filter Category=Contract
FREEASTRO_TEST_API_KEY=... dotnet test tests/FreeAstroApi.Tests --filter Category=Live
```

Without a local .NET SDK, run any of these in the pinned SDK container: `scripts/dotnet.sh scripts/check.sh` (set `SYNC_BACK="src/FreeAstroApi/Generated"` to copy regenerated files back).

- Builds treat warnings as errors and are deterministic; packages include SourceLink and a `.snupkg` symbol package.
- `tests/fixtures/synthetic-spec.json` exercises spec features production does not use yet (`ErrorResponse` schemas, 201/default responses, vendor `+json`, SVG/PNG/PDF/Markdown bodies, sanitised schema names); `scripts/check-fixture.sh` generates and compiles an SDK from it.
- CI: `ci.yml` (checks plus Prism contract tests of all 142 operations), `sync-spec.yml` (daily: fetch the production spec, regenerate, check, open or update a `spec-sync/auto` pull request), `live-smoke.yml` (daily read-only calls when `FREEASTRO_TEST_API_KEY` is set), `release.yml` (publishes on `v*` tags).

## Releasing

Bump `<Version>` in `src/FreeAstroApi/FreeAstroApi.csproj`, update `CHANGELOG.md`, then push a matching tag (`git tag v0.1.0 && git push origin v0.1.0`). The `Release` workflow runs the checks and publishes to nuget.org with trusted publishing.

Issues and contributions are welcome on [GitHub](https://github.com/GabrielRw/freeastroapi-dotnet-sdk).

Licensed under the Apache License 2.0.
