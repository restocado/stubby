# Stubby : Extraction Prompt

Date: 2026-10-17  
Author: restocado

The prompt and reply format the receipt extractor uses (see `docs/spec/extraction/02-receipt-extractor.md`). The prompt text lives in `ExtractionPrompt` in `Infrastructure/Extraction/`. This note explains it and records each version. If the two disagree, the code is right.

## How a change is made

The prompt version is stored with every receipt (`Receipts.PromptVersion`) and with every field record, so an accuracy number can be traced to the wording that produced it (NFR-M3).

1. Change the text in `ExtractionPrompt.Text` and change `ExtractionPrompt.Version`.
2. Update the pinned hash in `VisionReceiptExtractorTests`. The test `Prompt_text_is_pinned_to_its_version` fails until both are changed.
3. Add a row to the version history below.
4. Rerun the 30-receipt eval set and record the result in the row.

## Reply format

One JSON object, no markdown, with exactly these keys. Every value may be `null`.

| Key | Type | Meaning |
|---|---|---|
| `merchant` | string | The shop or business name, as printed. |
| `date` | string `yyyy-MM-dd` | The purchase date. `null` when day and month are ambiguous. |
| `total` | number | The final amount paid, including tax and any tip, after any discount. |
| `subtotal` | number | The amount before tax, only if printed. Used only for the flag check. |
| `tax` | number | The total tax, only if printed. Used only for the flag check. |

Numbers use a dot as the decimal separator, with no currency symbol and no thousands separator.

## How the reply is read

- The first `{` to the last `}` is parsed, so a markdown fence or a sentence around the JSON is tolerated. The model wrapped its JSON in a fence in a live test even though the prompt said not to.
- A key that is missing, `null` or unreadable is a missing field, never an error. A date that is not `yyyy-MM-dd` is missing. A number may also arrive as text (`"19.99"`, `"$19.99"`, `"1,234.50"`); other text is missing.
- Money is rounded to two decimals, and a value that does not fit `numeric(15,2)` is missing.
- The reply fails as a whole when it holds no JSON object, or when merchant, date and total are all missing. A receipt that fails this way becomes Failed and can be entered by hand.

## Choices worth knowing

- **Ambiguous dates are `null`.** `03/09/2026` could be 3 September or 9 March. A wrong date saved without a flag is worse than a missing one, which is flagged (`DateMissing`) and fixed in review.
- **The total includes the tip.** For receipts with a tip line, a discount or several total-like lines, the amount actually paid is the total.
- **Never guess.** The prompt says to use `null` rather than invent a value, so a blurry receipt gives a flagged result for the user to fix and not a confident wrong one.

## Version history

| Version | Date | Change | Eval result |
|---|---|---|---|
| v1 | 2026-10-17 | First prompt. | Informal check, not the eval set: 8 clean generated receipts (photos, one tilted, and PDFs including a 2-page one) on `gemma4:31b`, 40 of 40 fields right, about 1 s a call. The ambiguous `03/09/2026` date came back `null`, as asked. The 30-receipt eval set has not been run. |
