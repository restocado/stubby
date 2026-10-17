# Reprocess Receipts

**Status:** Draft  
**Location:** `Application/Features/Receipts/ReprocessReceipt/`: `ReprocessReceiptCommand`, `ReprocessReceiptCommandHandler`. And `Application/Features/Receipts/ReprocessAllFailedReceipts/`: `ReprocessAllFailedReceiptsCommand`, `ReprocessAllFailedReceiptsCommandHandler`, `ReprocessAllFailedReceiptsResponse`. Both are Mediator commands that use `IReceiptRepository.ReprocessAsync` and `ReprocessAllFailedAsync`. The responses are result values: `ReprocessReceiptResponse` is `Requeued`, `NotFound` or `Conflict`, and the "all" response carries `RequeuedCount`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Put one or all Failed receipts back in the queue with a fresh set of attempts.

## Behavior

- **One:** unknown id is `NotFound`; any status other than Failed is a `Conflict`. In one transaction the row is locked, the status is checked again, and `Receipt.Reprocess` is called, which resets `AttemptsUsed` and `LastError`. Same file, no new upload. Two simultaneous reprocesses of one receipt requeue it once, and the other is a `Conflict`.
- **All:** in one transaction, locks every Failed row, reprocesses each with `Receipt.Reprocess` in one `SaveChanges`, and returns the count. Zero is not an error. Other statuses are untouched.
- The background worker picks a requeued receipt up like any Queued one, in `CreatedAt` order.
- **UI:** a Reprocess button on every Failed row of the list and on the review page of a Failed receipt, and a "Reprocess all failed" button above the list. A message says what happened, for example "TXN-000042 is back in the queue." or "3 receipts are back in the queue." or "There are no failed receipts." From the review page a requeued receipt goes back to the list, because it cannot be opened until it has been read again.

## Acceptance criteria

- A Failed receipt with `AttemptsUsed = 3` becomes Queued with `AttemptsUsed = 0` and no `LastError`.
- Other statuses return conflict and change nothing. Unknown id returns not found.
- With 5 Failed, 2 ForReview and 1 Approved, "all" requeues 5 and returns 5.
- With no Failed receipts, "all" returns 0.
- A requeued receipt keeps its `CreatedAt` and its `TransactionNo`.
- If saving fails for "all", nothing is requeued. This rests on the single transaction and is not tested with a forced failure, unlike approval.
