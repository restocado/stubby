# Extraction Module : Overview

**Status:** Built

Reading receipts in the background: worker, processing, PDF rendering, extractor, AI gateway and flagging rules.

Follows `docs/architecture.md`: one feature slice per command or query. The AI gateway knows nothing about receipts. The extractor owns the prompt and parsing and sits behind `IReceiptExtractor` in Application, implemented in Infrastructure.

| File | Contents |
|---|---|
| [01-ai-gateway.md](01-ai-gateway.md) | Provider access, timeout, rate limit, circuit breaker |
| [02-receipt-extractor.md](02-receipt-extractor.md) | Prompt, parsing, result |
| [03-pdf-rendering.md](03-pdf-rendering.md) | PDF pages to images |
| [04-flagging-rules.md](04-flagging-rules.md) | When a receipt is flagged |
| [05-process-next-receipt.md](05-process-next-receipt.md) | Claim, extract, record outcome |
| [06-extraction-worker.md](06-extraction-worker.md) | Hosted service and restart recovery |
| [07-test-plan.md](07-test-plan.md) | Cross-cutting tests |
| [08-open-decisions.md](08-open-decisions.md) | Open items |

**Build order:** 01, 03 and 04 have no dependencies. Then 02, then 05, then 06.
