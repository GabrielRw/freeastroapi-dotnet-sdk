# Changelog

## 0.1.0

Initial release of the official .NET SDK (`FreeAstroApi.Sdk`).

- `FreeAstroClient` with `FreeAstroClientOptions` (`ApiKey` with `FREEASTRO_API_KEY` fallback, `BaseUrl`, `Timeout`, `MaxRetries`, retry delays, default headers); targets `net8.0` and `netstandard2.0`.
- Typed async methods with `CancellationToken` for all 142 operations in the public OpenAPI document, grouped by API path (`client.Western.Natal.InsightsAsync`); the latest API version gets the plain name and older versions a `V1`/`V2` suffix, as in the TypeScript and Python SDKs. Deprecated operations are `[Obsolete]`.
- 623 request/response models and per-operation query parameter classes generated from `spec/freeastro.json`, serialized with System.Text.Json source generation; unknown response fields are kept in `AdditionalProperties`.
- SVG/PNG/PDF/Markdown responses as `string` or `FreeAstroContent`; `SendAsync<T>` and `SendRawAsync` for endpoints newer than the SDK.
- Retries with exponential backoff honouring `Retry-After` (429 for every method; 408/5xx, timeouts and network errors for GET and idempotent requests), per-request `RequestOptions` (headers, `IdempotencyKey`, timeout, retries).
- Typed exceptions: `FreeAstroValidationException`, `FreeAstroAuthenticationException`, `FreeAstroPermissionException`, `FreeAstroNotFoundException`, `FreeAstroConflictException`, `FreeAstroRateLimitException`, `FreeAstroServerException` (all `FreeAstroApiException` with `StatusCode`, `Body`, `RequestId`, `ErrorCode`), plus network, timeout and deserialization errors.
- `services.AddFreeAstro()` for ASP.NET Core / `IHttpClientFactory`.
- `User-Agent: freeastroapi-dotnet/<version>`; SourceLink, deterministic builds and a symbol package.
- Tooling: in-repo generator with a `--check` mode, the shared `spec_sync.py` / `with_prism.py` scripts, Prism contract tests, and CI, release (NuGet trusted publishing), daily spec-sync and live smoke workflows.
