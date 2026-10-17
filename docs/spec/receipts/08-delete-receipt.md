# Delete Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/DeleteReceipt/`: `DeleteReceiptCommand`, `DeleteReceiptCommandHandler`. Uses `IImageStore`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Remove a receipt and its image for good. Field records stay, unlinked (NFR-D2).

## Behavior

1. Unknown id: not found. Queued or Processing: conflict.
2. Delete the row in a transaction. Field records are not touched.
3. After commit, delete the image file. If that fails, log it (no receipt content) and still succeed.

An Approved receipt leaves the totals and the CSV. The checksum is freed, so the file can be uploaded again.

## Acceptance criteria

- ForReview, Failed and Approved receipts are deleted with their file.
- Queued and Processing return conflict and remove nothing.
- Field records of a deleted Approved receipt remain unchanged, with nothing that identifies the receipt.
- The deleted receipt's amount leaves its monthly total and the CSV.
- The same file can be uploaded again and gets a new `TransactionNo`. The deleted number is not reused.
- A failed file delete still returns success. A failed database delete leaves the file.
- The accuracy query returns the same counts before and after a delete.
