# Approve Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/ApproveReceipt/`: `ApproveReceiptCommand`, `ApproveReceiptCommandHandler`, `ApproveReceiptCommandValidator`, `ApproveReceiptResponse`. A Mediator command. Uses `IReceiptRepository.FindByIdAsync` and `ApproveAsync`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Save final values and category, write field records, lock the receipt.

## Behavior

Command: `ReceiptId`, `Merchant`, `Date`, `Total`, `Category`. The values are nullable, because they come from a form: a missing one is an error for its field.

The response is one of `Approved(TransactionNo)`, `NotFound`, `Conflict` or `Invalid(errors)`, where each error names its field (`Merchant`, `Date`, `Total` or `Category`) and holds a short message for the user.

1. Unknown id: `NotFound`. Status other than ForReview or Failed: `Conflict`. These come before the input is checked.
2. Validate: merchant not blank, date present, total present with at most two decimals and small enough for `numeric(15,2)`, category one of the eight. Every problem is reported at once.
3. `IReceiptRepository.ApproveAsync` loads the row with `FOR UPDATE` in one transaction, checks the status again (another tab may have approved or deleted it since step 1), calls `Receipt.Approve`, and saves the receipt and its 3 field records together. The row lock makes two simultaneous approvals of one receipt run one after the other, and the second one sees `Approved` and is a `Conflict`. If anything throws before the save, nothing is written.

Flagged receipts can be approved. A Failed receipt is a manual entry (`IsManual = true`, no extracted value, model or prompt version), and its form must fill all three fields.

Only a missing or malformed value blocks an approval. A total of zero or less, a date in the future and a very old date are all allowed: the flag is advice and the user has the last word. The merchant is trimmed.

## Acceptance criteria

- Approving a ForReview receipt unchanged sets Approved and writes 3 records where extracted equals final.
- A corrected total gives a Total record where extracted and final differ, and two matching records.
- A missing extracted date that the user fills in gives a record with a null extracted value.
- A Failed receipt gives 3 manual records and `IsManualEntry = true`.
- Approving an Approved, Queued or Processing receipt returns `Conflict` and writes nothing. An unknown id returns `NotFound`.
- Invalid input returns `Invalid` and writes nothing.
- A failure while saving leaves the receipt unchanged and writes no records.
- Two simultaneous approvals of one receipt approve it once, write 3 records and not 6, and the other returns `Conflict`.
- A flagged receipt can be approved, including one with a total of zero, a future date or a very old date.
- A total that fills the column (`9 999 999 999 999.99`) round-trips.
