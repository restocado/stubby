# Eval Module : Open Decisions

**Status:** Draft

Nothing is open now. Each question was answered with its recommendation, and the answers for the generator are built and written into [01-test-set-generator.md](01-test-set-generator.md). The two that belong to the run command are noted here until it is built.

| # | Question | Answer | Where |
|---|---|---|---|
| 1 | Which receipts are the extra single-page PDFs? | Some clean, no-tax and tip receipts, so 10 of the 30 are PDFs. | [01-test-set-generator.md](01-test-set-generator.md) |
| 2 | How is the date-flag receipt fixed in time? | A fixed "today" (2026-10-17) is stored in the answer file and passed to the flag evaluator. The date-flag receipt has no printed date. | [01-test-set-generator.md](01-test-set-generator.md) |
| 3 | Does a weak group fail the run if overall targets pass? | No. The run is gated on the overall targets, and groups are shown for information. | [02-run-eval.md](02-run-eval.md), not built yet |
| 4 | Run receipts one at a time or in parallel? | One at a time, to avoid the rate limit and circuit breaker. | [02-run-eval.md](02-run-eval.md), not built yet |
| 5 | Answer file format? | JSON, camel case, indented, with LF line ends. | [01-test-set-generator.md](01-test-set-generator.md) |

Also decided while building the generator: the test set lives in `testset/` at the repository root, it is drawn with SkiaSharp, and the work is split into two pull requests, the generator and then the run command.
