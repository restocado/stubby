# AI Gateway

**Status:** Draft  
**Location:** `Infrastructure/AiGateway/`: `AiGateway`, `OllamaCloudAdapter` (behind an internal `IAiProviderAdapter`), `AiGatewayOptions`, `AiGatewayRegistration`. `IAiGateway` (which also exposes the configured `Model`, so the extractor can record it), `AiRequest` and `AiGatewayException` in `Application/Abstractions/`.  
**Depends on:** nothing.

## Scope

The only code that talks to model providers. Sends images and a prompt, returns the reply text. Knows nothing about receipts.

## Behavior

- The model name comes from settings and maps to a provider adapter. First: `gemma4:31b` on Ollama Cloud. Changing the model needs no code change.
- HTTPS only (NFR-S2). The API key comes from user-secrets or an environment variable (NFR-S3).
- Applies a timeout, a rate limit and a circuit breaker, all from settings, built with `Microsoft.Extensions.Http.Resilience` on the provider's `HttpClient`. Outermost first: circuit breaker, timeout, rate limiter.
- The timeout is `TimeoutBaseSeconds + TimeoutPerImageSeconds × image count` (30 s + 15 s per image to start with). It covers any wait for the rate limiter, and a timeout counts as a failure for the circuit.
- A call over the rate limit **waits** for a free slot, up to the timeout. The limit is a token bucket: `RateLimitPerMinute` calls a minute (30), with a burst of `RateLimitBurst` (5).
- The circuit opens after `CircuitFailureThreshold` failures (5) with no success in `CircuitSamplingSeconds` (300), and stays open for `CircuitBreakSeconds` (60). Polly's breaker is ratio-based, not a count of consecutive failures, so it is set to a failure ratio of 1. Provider 5xx, 408 and 429 replies, timeouts and connection errors count. A rejected key (401) or other 4xx does not.
- **No retry.** The worker owns the attempt budget (NFR-R1). No fallback provider in v1.
- Logs model, duration, outcome, image count and token counts per call. Never logs prompts, images or replies.
- Every failure (provider error, timeout, rate limit, open circuit) surfaces as `AiGatewayException` with a short reason. A caller's own cancellation is not wrapped.
- A missing API key fails the call, not startup, so the rest of the app runs without one. Set it with `dotnet user-secrets set "AiGateway:OllamaCloud:ApiKey" "<key>" --project src/Web`, or the environment variable `AiGateway__OllamaCloud__ApiKey`. `appsettings.json` holds a blank `ApiKey` as a placeholder, which counts as missing. The key never goes in that file.
- Settings are read once at startup, so a new model name or limit needs a restart. `AiGateway:Models` is a list of name and provider pairs and not a dictionary, because a model name such as `gemma4:31b` has a colon, which configuration reads as a section separator.
- The provider call is Ollama's native `POST api/chat` with `stream: false` and the images as base64. The reply's `prompt_eval_count` and `eval_count` are the token counts that are logged. No sampling options are sent yet.

## Acceptance criteria

- A fake provider reply is returned as text.
- Changing the model setting changes the model sent.
- A response slower than the timeout fails and cancels the HTTP call.
- A provider error fails with the provider called exactly once.
- After the configured run of failures the next call fails without reaching the provider, and one call is let through after the break period.
- A non-HTTPS URL is rejected at startup.
- No log line contains image bytes, prompt or reply text, or the API key.
- The gateway references no receipt types.
