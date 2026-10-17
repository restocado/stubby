# Extraction Module : Open Decisions

**Status:** Draft

None are decided until the author confirms them.

Decided and moved into the specs: #2 (a call over the rate limit waits, up to the timeout) and #3 (starting timeout, rate limit and circuit values), both in [01-ai-gateway.md](01-ai-gateway.md), and #5 (150 DPI to start, a setting), in [03-pdf-rendering.md](03-pdf-rendering.md). #1 (prompt and reply format) is written up in [docs/extraction-prompt.md](../../extraction-prompt.md) and #4 (one unparseable field is a missing field) is in [02-receipt-extractor.md](02-receipt-extractor.md). "Nothing usable" in #4 is taken to mean merchant, date and total all missing, which fails the reply and makes the receipt Failed. #6 (today is the server's local date), #7 (requeued receipts go in `CreatedAt` order, revisit if "reprocess all" delays new uploads) and #8 (`MaxAttempts` is re-read on every call) are in [05-process-next-receipt.md](05-process-next-receipt.md). Keeping the `SubtotalTaxMismatch` flag for receipts with a tip is noted in [04-flagging-rules.md](04-flagging-rules.md).

| # | Question | Blocks | Recommendation |
|---|---|---|---|
| 9 | Should the worker pause while the circuit is open? | 06 | No, as decided in the architecture. Watch the usage logs. |
