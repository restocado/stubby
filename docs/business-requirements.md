# Stubby : Business Requirements Document

Date: 2026-10-17  
Author: restocado

## Problem

People who track spending keep a pile of receipts and type each one into a spreadsheet by hand. It is tedious, so they fall behind, and by month-end they are guessing at totals and categories.

## Goal

Take a photo of a receipt, check the details Stubby picked up, and see where this month's money went.

## How it works

1. Upload a receipt image (JPG, PNG, or PDF). Any other file type is rejected with an error and never queued. A file whose checksum matches an existing receipt, approved or not, is a duplicate: the upload fails with an error and the file is never queued. The receipt is added to the processing queue automatically with the status Queued.
2. A background job picks up the receipt (status Processing) and sends the image, or every page of a PDF, to the vision model, which fills in the merchant, date, and total. When the receipt shows a subtotal and a tax, they are read too, but only to check the total. They are not stored or shown. While the receipt is Queued or Processing the user cannot open it. The user can keep uploading other receipts.
3. When extraction works, the receipt becomes For review, and it is flagged if the total looks wrong. If extraction does not work, the receipt is put back in the queue and retried automatically until the maximum number of attempts per receipt is used. That maximum is a setting, 1 to start with, which means no automatic retry. When the last attempt fails, the receipt becomes Failed.
4. A receipts list shows all receipts, newest first, with their status (Queued, Processing, Failed, For review, Approved). The user opens a For review or Failed receipt from this list.
5. A review screen shows the receipt beside the extracted fields. The user confirms or corrects them and picks the category from a fixed list: groceries, dining, transport, utilities, shopping, health, entertainment, other. For a Failed receipt the user enters the details manually.
6. Approved receipts feed monthly totals by category, with CSV export. Approved receipts can be viewed from the list but can never be edited.
7. The user can delete a receipt that is For review, Failed or Approved. Deleting removes the receipt and its image for good. The field records from its review are kept without any link to the receipt, so the accuracy report stays complete. Deleting also takes it out of the monthly totals if it was Approved, and allows the same file to be uploaded again. Queued and Processing receipts cannot be deleted.
8. The user can reprocess a Failed receipt, or reprocess all Failed receipts at once. Each one goes back to Queued and gets a fresh set of attempts, up to the configured maximum. The user can also choose to enter the details manually instead.

## Receipt statuses

A receipt moves through these statuses. Queued and Processing receipts are locked, so the user cannot open or delete them. Only Approved receipts feed the monthly totals. When a receipt is approved, every extracted field (merchant, date and total) is stored with the field name, the extracted value, the final value, the time, and the model and prompt version, whether or not the user changed it. Details the user enters by hand on a Failed receipt are stored too, but tagged as manual and left out of accuracy numbers. These records are kept even if the receipt is deleted, but only as an anonymous record with no image or other receipt details.

```mermaid
flowchart LR
    Q[Queued] --> P[Processing]
    P --> OK{Extraction<br/>worked?}
    OK -- Yes --> R[For review<br/>flagged if total looks wrong]
    OK -- No --> RT{Attempts used<br/>less than the maximum?}
    RT -- Yes --> Q
    RT -- No --> F[Failed<br/>user enters details manually]
    F -- user reprocesses --> Q
    R --> A[Approved<br/>read-only, never editable]
    F --> A
```

## Users

People who want a monthly spending view without typing receipts into a spreadsheet.

## Out of scope

- Bank or card account syncing
- Mobile app (the web UI only needs to work in a phone browser)
- Multi-currency and tax-filing exports
- Shared or multi-user budgets
- Handwritten receipts
- Login or user accounts (the app is single-user for now)
- A retention policy for receipts and extracted data (nothing is deleted automatically for now)
- Custom categories (the category list is fixed for now)

## Success criteria

1. The total is extracted correctly on at least 90% of a set of 30 synthetic receipts.
2. Merchant and date are correct on at least 85% of the same set.
3. Of the receipts where extraction got the total wrong, at least 95% are flagged for review instead of being saved silently.
4. Median time from upload to a reviewed, saved receipt is under 60 seconds for a single-page receipt.
5. Every reviewed field is stored with its extracted and final value, so a before/after accuracy report can be generated from the stored data. Manual entries are excluded from it. There is no accuracy screen in v1.

## Test set

The 30 synthetic receipts are generated by a script from invented values, so the correct answers are known exactly and no real receipts are used. The generator uses a fixed seed, so a rerun produces the same receipts. It writes the images and an answer file. The set is kept in the repo, so a model change can be scored on the same inputs.

| Group | Count | Purpose |
|---|---|---|
| Clean receipts across 6 layouts, with mixed fonts and date formats | 14 | Baseline accuracy |
| No tax shown | 4 | Receipts without tax |
| Tip, discount, or several total-like lines | 4 | Hard cases for the total and for flagging |
| Multi-page PDF | 2 | Whole-PDF reading |
| Blurry or phone-photo style (blur, tilt, low light, noise) | 4 | The blurry photos |
| Total at or above the flag limit | 1 | Flag limit |
| Date missing, in the future, or over a year old | 1 | Date flag |

About 20 are JPG or PNG and 10 are PDF.

## Constraints and assumptions

- .NET 10 and Blazor
- Fake or synthetic receipts only for the demo
- Extraction uses a vision model behind an interface, so the provider can change
- The first provider is Ollama Cloud, with gemma4:31b as the first model. The model is a setting and can be changed without a code change
- The queue must be stored durably so receipts survive an app restart
- The background job is a .NET hosted service that reads from a database-backed queue
