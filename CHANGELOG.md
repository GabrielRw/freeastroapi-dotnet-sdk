# Changelog

## 0.1.0

Initial release of the official .NET SDK (`FreeAstroApi.Sdk`).

- `FreeAstroClient` with `FreeAstroClientOptions` (`ApiKey` with `FREEASTRO_API_KEY` fallback, `BaseUrl`, `Timeout`, `MaxRetries`, retry delays, default headers); targets `net10.0`, `net8.0` and `netstandard2.0`.
- Typed async methods with `CancellationToken` for all 142 operations in the public OpenAPI document, grouped by API path (`client.Western.Natal.InsightsAsync`); the latest API version gets the plain name and older versions a `V1`/`V2` suffix, as in the TypeScript and Python SDKs. Deprecated operations are `[Obsolete]`.
- Typed responses for all operations: 975 request/response models and per-operation query parameter classes generated from `spec/freeastro.json`, serialized with System.Text.Json source generation. 124 operations return a typed model, 13 return SVG/PNG/PDF bodies as `FreeAstroContent` or `string`, and the 5 secondary/tertiary/quaternary/quotidian/converse progression endpoints return a `JsonElement` because their response is one of two models depending on the request (both are named in the XML docs).
- Lenient response decoding: a missing required field, `null` in a non-nullable number or boolean, an unknown field (kept in `AdditionalProperties`) or an unknown enum value (enum-like fields are strings) never throws. Request models are unchanged.
- `SendAsync<T>` and `SendRawAsync` for endpoints newer than the SDK.
- One retry policy shared by every FreeAstroAPI SDK: `MaxRetries = 2`, exponential backoff from 500 ms (factor 2, full jitter, capped at 8 s by `MaxBackoff`). HTTP 429 is retried for every operation, honouring `Retry-After` (seconds or HTTP date) and `retry_after_ms`; a wait over 30 s (`MaxRetryDelay`) is not waited out but thrown with `RetryAfter`. HTTP 408/500/502/503/504, timeouts and network errors are retried for every operation except the side-effecting ones in `FreeAstroOperations.SideEffectingOperationIds` (`v1_natal_calculate`, `v1_ai_generate`, `v2_vedic_chat`, `v2_western_chat`, `v2_vedic_qa`, `v1_western_transits_timeline`, `v1_western_transits_insights`), which need an `IdempotencyKey` or `Retry = true`. Other 4xx are never retried. Per-request `RequestOptions` (headers, `IdempotencyKey`, timeout, `MaxRetries`, `Retry`).
- Typed exceptions: `FreeAstroValidationException`, `FreeAstroAuthenticationException`, `FreeAstroPermissionException`, `FreeAstroNotFoundException`, `FreeAstroConflictException`, `FreeAstroRateLimitException`, `FreeAstroServerException` (all `FreeAstroApiException` with `StatusCode`, `Body`, `RequestId`, `ErrorCode`, `RetryAfter` and the parsed `ErrorResponse` body in `Error`; route errors with an object `detail` report its `error` code and `message`), plus network, timeout and deserialization errors.
- `services.AddFreeAstro()` for ASP.NET Core / `IHttpClientFactory`.
- `User-Agent: freeastroapi-dotnet/<version>`; SourceLink, deterministic builds and a symbol package.
- Tooling: in-repo generator with a `--check` mode, the shared `spec_sync.py` / `with_prism.py` scripts, Prism contract tests, and CI, release (NuGet trusted publishing), daily spec-sync and live smoke workflows.
