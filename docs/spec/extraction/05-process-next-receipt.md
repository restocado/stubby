# Process Next Receipt

**Status:** Draft  
**Location:** `Application/Features/Extraction/ProcessNextReceipt/`: `ProcessNextReceiptCommand`, `ProcessNextReceiptCommandHandler`, `ProcessNextReceiptResult`. Settings: `MaxAttempts`, `FlagTotalLimit`.  
**Depends on:** 02-receipt-extractor, 04-flagging-rules, receipts 01 and 02.

## Scope

Take the oldest Queued receipt, extract it, record the outcome. One call handles one receipt.

## Behavior

1. **Claim** the oldest Queued receipt by `CreatedAt` in one transaction with `FOR UPDATE SKIP LOCKED` (raw SQL), then `StartProcessing`. If none, return "nothing to do".
2. Read the file and call the extractor.
3. **Success:** run the flag rules, then `CompleteExtraction`. Subtotal and tax are dropped.
4. **Failure** (extractor, gateway or PDF error, missing file, timeout): `FailAttempt(reason, MaxAttempts)`. `LastError` has no receipt content.
5. **Cancellation on shutdown:** `ReturnToQueue`, no attempt charged.
6. Never let one bad receipt throw to the worker. Unexpected errors are logged and count as a failed attempt.

`MaxAttempts` starts at 1, which means no retry.

## Acceptance criteria

- A success ends ForReview with extracted fields stored and `AttemptsUsed = 0`.
- With no Queued receipts, nothing changes.
- With three Queued receipts, the oldest is processed first.
- Total 3500.00 with limit 3000 ends ForReview flagged. A missing total ends ForReview flagged, not Failed.
- With max 1 and a failing extractor: Failed, `AttemptsUsed = 1`, extractor called once.
- With max 3: Queued, Queued, Failed across three calls.
- A missing file and a gateway timeout each count as a failed attempt.
- Cancelling mid-extraction leaves Queued with `AttemptsUsed` unchanged.
- Two concurrent calls with one Queued receipt: one processes it, the other does nothing.
- Subtotal and tax appear nowhere in the database. No log line contains receipt content.
