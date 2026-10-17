# AI Gateway

**Status:** Draft  
**Location:** `Infrastructure/AiGateway/`: `AiGateway`, `OllamaCloudAdapter`, `AiGatewayOptions`. Interface `IAiGateway` in Application.  
**Depends on:** nothing.

## Scope

The only code that talks to model providers. Sends images and a prompt, returns the reply text. Knows nothing about receipts.

## Behavior

- The model name comes from settings and maps to a provider adapter. First: `gemma4:31b` on Ollama Cloud. Changing the model needs no code change.
- HTTPS only (NFR-S2). The API key comes from user-secrets or an environment variable (NFR-S3).
- Applies a timeout, a rate limit and a circuit breaker, all from settings.
- **No retry.** The worker owns the attempt budget (NFR-R1). No fallback provider in v1.
- Logs model, duration, outcome, image count and token counts per call. Never logs prompts, images or replies.
- Every failure (provider error, timeout, rate limit, open circuit) surfaces as `AiGatewayException` with a short reason.

## Acceptance criteria

- A fake provider reply is returned as text.
- Changing the model setting changes the model sent.
- A response slower than the timeout fails and cancels the HTTP call.
- A provider error fails with the provider called exactly once.
- After the configured run of failures the next call fails without reaching the provider, and one call is let through after the break period.
- A non-HTTPS URL is rejected at startup.
- No log line contains image bytes, prompt or reply text, or the API key.
- The gateway references no receipt types.
