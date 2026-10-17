# Delete Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/DeleteReceipt/`: `DeleteReceiptCommand`, `DeleteReceiptCommandHandler`, `DeleteReceiptResponse` (`Deleted`, `NotFound` or `Conflict`). A Mediator command. Uses `IReceiptRepository.DeleteAsync` and `IImageStore`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Remove a receipt and its image for good. Field records stay, unlinked (NFR-D2).

## Behavior

1. Unknown id: `NotFound`. Queued or Processing: `Conflict`. This is checked inside the transaction, with the row locked.
2. Delete the row in a transaction. Field records are not touched: they hold no link to the receipt, so there is nothing to break. The repository returns the stored file's path and the transaction number.
3. After commit, delete the image file. If that fails, log it (no receipt content: only the transaction number) and still succeed. The image store logs a failed delete itself and does not throw, and the handler also catches anything it might throw.
4. If the database delete fails, the file is not touched.
5. A delete and an approval of the same receipt at the same moment run one after the other, because both lock the row. Either the approval ran first and its three records stay, or the delete ran first and the approval finds nothing. Neither leaves a half-done state.
6. **UI:** a Delete button on every row of the list that is not Queued or Processing, and on the review page of a Failed receipt. It asks "Delete TXN-000042?" in place, with "Yes, delete" and "Cancel", and does nothing until confirmed. A message says what happened.

An Approved receipt leaves the totals and the CSV. The checksum is freed, so the file can be uploaded again.

## Acceptance criteria

- ForReview, Failed and Approved receipts are deleted with their file.
- Queued and Processing return conflict and remove nothing.
- Field records of a deleted Approved receipt remain unchanged, with nothing that identifies the receipt.
- The deleted receipt's amount leaves its monthly total and the CSV. (The reports are not built yet: both read the Approved rows of `Receipts`, so a deleted row is gone from them by construction.)
- The same file can be uploaded again and gets a new `TransactionNo`. The deleted number is not reused.
- A failed file delete still returns success. A failed database delete leaves the file.
- The accuracy query returns the same counts before and after a delete.
