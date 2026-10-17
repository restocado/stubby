# Eval Module : Open Decisions

**Status:** Draft

None are decided until the author confirms them.

| # | Question | Blocks | Recommendation |
|---|---|---|---|
| 1 | Which receipts are the extra single-page PDFs? The BRD lists only 2 multi-page ones. | 01 | Make some clean, no-tax and tip receipts PDFs so about 10 are PDF. |
| 2 | How is the date-flag receipt fixed in time? | 01, 02 | Store a fixed "today" in the answer file and pass it to the flag evaluator. |
| 3 | Does a weak group fail the run if overall targets pass? | 02 | No. Gate on overall targets and show groups for information. |
| 4 | Run receipts one at a time or in parallel? | 02 | One at a time, to avoid the rate limit and circuit breaker. |
| 5 | Answer file format? | 01, 02 | JSON. |
