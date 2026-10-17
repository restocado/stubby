# Test Set Generator

**Status:** Draft  
**Location:** `Eval/Features/Generate/`: `GenerateTestSetCommand`, `GenerateTestSetCommandHandler`, `GenerateOptions` (output folder, seed), one renderer per layout. Output is committed to the repo.  
**Depends on:** nothing.

## Scope

Generate the 30 synthetic receipts and an answer file from invented values (NFR-D4).

## Behavior

- A fixed seed gives identical receipts and answer file on every run.
- Values are invented. Merchant names come from an invented list.
- Output: one file per receipt (JPG, PNG or PDF) and `answers.json`. Each entry has the file name, group, correct merchant, date, total, subtotal and tax where shown, and expected flag and reasons.

| Group | Count |
|---|---|
| Clean, 6 layouts, mixed fonts and date formats | 14 |
| No tax shown | 4 |
| Tip, discount or several total-like lines | 4 |
| Multi-page PDF | 2 |
| Blurry or phone-photo style | 4 |
| Total at or above the flag limit | 1 |
| Date missing, in the future or over a year old | 1 |

About 20 are JPG or PNG and about 10 are PDF. The other PDFs are single-page receipts from the other groups. The answer file stores a fixed "today" for the date flag.

## Acceptance criteria

- Exactly 30 receipt files and one answer file, with the group counts above.
- 18 to 22 JPG or PNG, 8 to 12 PDF, and both multi-page PDFs have at least 2 pages.
- Six distinct layouts in the clean group.
- Two runs with the same seed give identical files. A different seed gives different values.
- Subtotal + tax equals the total unless the case is built to mismatch.
- The over-limit and date-flag receipts have `ExpectedFlagged = true`.
- No value comes from a real receipt.
- Git shows no change after a rerun with the same seed.
