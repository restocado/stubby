# Approve Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/ApproveReceipt/`: `ApproveReceiptCommand`, `ApproveReceiptCommandHandler`, `ApproveReceiptCommandValidator`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Save final values and category, write field records, lock the receipt.

## Behavior

Command: `ReceiptId`, `Merchant`, `Date`, `Total`, `Category`.

1. Unknown id: not found. Status other than ForReview or Failed: conflict.
2. Validate: merchant not blank, date present, total present with at most two decimals, category one of the eight.
3. Call `Receipt.Approve`. Save the receipt and its 3 field records in one `SaveChanges`.

Flagged receipts can be approved. A Failed receipt is a manual entry (`IsManual = true`, no extracted value, model or prompt version).

## Acceptance criteria

- Approving a ForReview receipt unchanged sets Approved and writes 3 records where extracted equals final.
- A corrected total gives a Total record where extracted and final differ, and two matching records.
- A missing extracted date that the user fills in gives a record with a null extracted value.
- A Failed receipt gives 3 manual records and `IsManualEntry = true`.
- Approving an Approved, Queued or Processing receipt returns conflict and writes nothing.
- Invalid input writes nothing.
- A failure while saving leaves the receipt unchanged.
- A flagged receipt can be approved.
