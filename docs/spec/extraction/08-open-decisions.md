# Extraction Module : Open Decisions

**Status:** Draft

None are decided until the author confirms them.

Decided and moved into the specs: #2 (a call over the rate limit waits, up to the timeout) and #3 (starting timeout, rate limit and circuit values), both in [01-ai-gateway.md](01-ai-gateway.md), and #5 (150 DPI to start, a setting), in [03-pdf-rendering.md](03-pdf-rendering.md).

| # | Question | Blocks | Recommendation |
|---|---|---|---|
| 1 | What are the exact prompt and reply format? Neither is written yet. | 02, 05 | JSON with fixed field names, `null` for unreadable fields, `yyyy-MM-dd` dates, no currency symbol. Write the prompt as its own design note. |
| 4 | Is one unparseable field a missing field or a failed reply? | 02 | Missing field. Fail only when nothing is usable. |
| 6 | Which time zone defines "today" for the date flags? | 04, 05 | The server's local date. |
| 7 | In what order are requeued receipts processed? | 05 | By `CreatedAt`. Revisit if "reprocess all" delays new uploads. |
| 8 | Is `MaxAttempts` re-read on every call or only at startup? | 05, 06 | Every call. |
| 9 | Should the worker pause while the circuit is open? | 06 | No, as decided in the architecture. Watch the usage logs. |
