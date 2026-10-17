# Reprocess Receipts

**Status:** Draft  
**Location:** `Application/Features/Receipts/ReprocessReceipt/`: `ReprocessReceiptCommand`, `ReprocessReceiptCommandHandler`. And `Application/Features/Receipts/ReprocessAllFailedReceipts/`: `ReprocessAllFailedReceiptsCommand`, `ReprocessAllFailedReceiptsCommandHandler`, `ReprocessAllFailedReceiptsResponse`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Put one or all Failed receipts back in the queue with a fresh set of attempts.

## Behavior

- **One:** unknown id is not found; any status other than Failed is a conflict. Calls `Receipt.Reprocess`. Same file, no new upload.
- **All:** loads every Failed receipt, reprocesses each in one `SaveChanges`, returns the count. Zero is not an error. Other statuses are untouched.

## Acceptance criteria

- A Failed receipt with `AttemptsUsed = 3` becomes Queued with `AttemptsUsed = 0` and no `LastError`.
- Other statuses return conflict and change nothing. Unknown id returns not found.
- With 5 Failed, 2 ForReview and 1 Approved, "all" requeues 5 and returns 5.
- With no Failed receipts, "all" returns 0.
- A requeued receipt keeps its `CreatedAt` and its `TransactionNo`.
- If saving fails for "all", nothing is requeued.
