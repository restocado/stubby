# Run Eval

**Status:** Draft  
**Location:** `Eval/Features/Run/`: `RunEvalCommand`, `RunEvalCommandHandler`, `RunEvalOptions`, `EvalScorer`, `EvalReport`. Uses `IReceiptExtractor` and `ReceiptFlagEvaluator`.  
**Depends on:** 01-test-set-generator, extraction 02 and 04.

## Scope

Run the 30 receipts through the extractor and report accuracy, so a model or prompt change can be measured (NFR-M3).

## Behavior

- For each receipt, call the extractor and then the flag evaluator with the configured limit and the stored "today". Receipts run one at a time.
- An extractor exception counts as wrong on every field, and the total counts as wrong and unflagged. The receipt is listed in the report.
- **Scoring:** total and date must match exactly. Merchant matches ignoring case, spacing and punctuation. Flag coverage is wrong totals that were flagged divided by all wrong totals, or "not applicable" if none are wrong.
- **Report:** per-field accuracy, flag coverage, results per group, every wrong field with expected and actual values, model and prompt version, and pass or fail against total ≥ 90%, merchant and date ≥ 85%, flag coverage ≥ 95%.
- Output goes to the console and a report file. Exit code 0 when all targets pass, otherwise non-zero.
- Model and prompt version come from the same settings as the app. The API key comes from user-secrets or an environment variable.

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
