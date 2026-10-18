# Test Set Generator

**Status:** Built  
**Location:** `Eval/Features/Generate/`: `GenerateTestSetCommand`, `GenerateTestSetCommandHandler`, `GenerateOptions` (output folder, seed), and under it `Content/` (the seeded plan of the 30 receipts), `Layouts/` (one class for each of the six layouts, and the `Sheet` they draw on), `PhotoEffect` and `PdfWriter`. The answer file types are in `Eval/Answers/AnswerFile.cs`, so the run command reads what the generator wrote. Output is committed to the repo, in `testset/` at the repository root.  
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

### How the set is built

- **Drawing:** SkiaSharp, which the PDF renderer already brings. Each layout draws a page as a bitmap, and the same bitmap is what is saved as a PNG or JPG, or embedded in a PDF. A PDF is written by hand, one JPEG per page, so it tests reading a PDF, its page order and a long file. It does not test text extraction, which the extractor does not use because it renders every PDF to pictures.
- **Layouts:** thermal till roll (typewriter font), café slip (sans-serif), supermarket roll (capitals and a barcode), serif invoice (table and boxed total), restaurant bill, and fuel slip. The clean group has 3, 3, 2, 2, 2 and 2 of them.
- **Groups:** 1-14 clean (4 are PDFs), 15-18 no tax (2 PDFs), 19-22 tip, discount, or a tendered-and-change or savings line that looks like a total (2 PDFs), 23-24 multi-page PDFs (two and three pages, 18 items to a page), 25-28 phone photos (tilt and blur, low light and noise, glare, motion smear), 29 over the limit (3 697.54, printed with a thousands separator), 30 no date printed. That is 20 pictures and 10 PDFs.
- **Files:** `01-clean-thermal.png` and so on: number, group, layout. `answers.json` holds `seed`, `today` (2026-10-17), `totalLimit` (3000) and one entry for each receipt.
- **Expected flags** are worked out by running the real `ReceiptFlagEvaluator` on the correct answers, so they cannot drift from the rules. Because of that rule, the tip and discount receipts are expected to be flagged `SubtotalTaxMismatch`: their subtotal and tax do not add up to the total, and that is accepted behaviour (see the flagging spec). The tender-and-change and savings receipts add up and are not flagged.
- **Dates:** every date is in the past and within a year of "today", and none is ambiguous. A date such as `03/09/2026` could be day first or month first, and the prompt tells the model to answer `null` for it, so numeric dates only use days over 12. The formats are `2026-10-02`, `02 Oct 2026`, `Oct 2, 2026`, `21.09.2026`, `09/21/2026` and `2 October 2026`.
- **Invented data:** merchant names come from a list in the generator. Addresses use obviously fictional street names, and telephone numbers are in the 555-01xx range kept for fiction. No card or account number is printed: a card receipt says "PAID BY CARD" and nothing more.
- **Photos:** the effects are mild enough for a person to read every line (checked by looking at all four). A tilted photo makes the amount column slope away from its labels, as a real one does.
- **Determinism:** two runs with the same seed give identical bytes on the same machine. Text is drawn with the machine's fonts (Courier New, Arial and Times New Roman, or the closest the system has), so a different operating system can draw the glyphs slightly differently. The committed files are the reference: the run command scores against them and the answer file, not against a fresh render. Regenerating on another system may show a diff in the pictures, and the answer file does not change.
- **Rerunning** removes the earlier numbered receipt files and `answers.json` in the folder first, and leaves any other file alone. `.gitattributes` marks the pictures and PDFs binary and `answers.json` as LF text.

## Acceptance criteria

- Exactly 30 receipt files and one answer file, with the group counts above.
- 18 to 22 JPG or PNG, 8 to 12 PDF, and both multi-page PDFs have at least 2 pages.
- Six distinct layouts in the clean group.
- Two runs with the same seed give identical files. A different seed gives different values.
- Subtotal + tax equals the total unless the case is built to mismatch.
- The over-limit and date-flag receipts have `ExpectedFlagged = true`.
- No value comes from a real receipt.
- Git shows no change after a rerun with the same seed.
