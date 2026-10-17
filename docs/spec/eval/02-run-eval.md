# Run Eval

**Status:** Draft  
**Location:** `Eval/Features/Run/`: `RunEvalCommand`, `RunEvalCommandHandler`, `RunEvalOptions`, `EvalScorer`, `EvalReport`. Uses `IReceiptExtractor` and `ReceiptFlagEvaluator`. Run it from the repository root with `dotnet run --project src/Eval -- run [--testset <folder>] [--report <file>]`.  
**Depends on:** 01-test-set-generator, extraction 02 and 04.

## Scope

Run the 30 receipts through the extractor and report accuracy, so a model or prompt change can be measured (NFR-M3).

## Behavior

- For each receipt, call the extractor and then the flag evaluator with the configured limit and the stored "today". Receipts run one at a time.
- An extractor exception counts as wrong on every field, and the total counts as wrong and unflagged. The receipt is listed in the report.
- **Scoring:** total and date must match exactly. Merchant matches ignoring case, spacing and punctuation. Flag coverage is wrong totals that were flagged divided by all wrong totals, or "not applicable" if none are wrong.
- **Report:** per-field accuracy, flag coverage, results per group, every wrong field with expected and actual values, model and prompt version, and pass or fail against total ≥ 90%, merchant and date ≥ 85%, flag coverage ≥ 95%.
- Output goes to the console and a report file (`eval-report.md` by default, ignored by git). The report is Markdown. A line for each receipt is printed as it is read. Exit code 0 when all targets pass, 1 when a target is missed, and 2 when the run could not be made: no `answers.json` in the test set folder, a missing or invalid setting, or the run was stopped with Ctrl+C. Nothing is written when it is stopped.
- Model and prompt version come from the same settings as the app. The tool uses the web app's own `appsettings.json` and its user-secrets, so the model, the limits and the API key are the same, and the key can also come from an environment variable. The model name is the gateway's configured one, the prompt version is the one the extractor reports, and when no receipt could be read at all the prompt version is shown as "unknown".
- The extractor is wired with `AddExtractor` (the gateway, the PDF renderer, the extractor and the extraction settings), so the tool needs no database and no file store.
- **Flags:** the flag limit is the app's `Extraction:FlagTotalLimit` setting and "today" is the one stored in `answers.json`. The expected flags in the answer file are not used for scoring: coverage asks whether the real rules flagged what was actually read.
- **Percentages** are worked out with whole numbers, so 27 of 30 is exactly 90% and 26 of 30 is below it.
- A failure other than a cancellation is scored as a failed read. The report shows the extraction's own short reason, and for an unexpected exception only its type, never its message.
- Receipts are read one at a time, in the order of the answer file, so that the run does not set off the gateway's rate limit or circuit breaker. A run of 30 receipts sends them to the model provider.

## Acceptance criteria

- An extractor that returns every answer exactly scores 100% and passes.
- 27 of 30 totals correct passes the total target. 26 fails, with a non-zero exit code.
- `ACME Foods, Inc.` matches `acme foods inc`. `ACME Food` does not.
- Total `12.5` equals `12.50`. Dates must be identical.
- 4 wrong totals with 3 flagged gives 75% coverage and fails.
- No wrong totals gives "not applicable" and does not fail.
- An exception on one receipt does not stop the run.
- The report shows model, prompt version, and all seven groups.
- No key appears in the output. The web app database is untouched.
