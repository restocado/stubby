# Process Next Receipt

**Status:** Draft  
**Location:** `Application/Features/Extraction/ProcessNextReceipt/`: `ProcessNextReceiptCommand`, `ProcessNextReceiptCommandHandler`, `ProcessNextReceiptResult`. Uses `IReceiptRepository` (`ClaimNextQueuedAsync`, `SaveAsync`), `IImageStore.ReadAsync`, `IReceiptExtractor` and `IExtractionSettings`. Settings `Extraction:MaxAttempts` (1) and `Extraction:FlagTotalLimit` (3000) in `appsettings.json`.  
**Depends on:** 02-receipt-extractor, 04-flagging-rules, receipts 01 and 02.

## Scope

Take the oldest Queued receipt, extract it, record the outcome. One call handles one receipt.

## Behavior

0. **Settings** are read first, on every call, before any receipt is claimed. A missing or invalid value throws, so a bad setting cannot leave a receipt Processing.
1. **Claim** the oldest Queued receipt by `CreatedAt` in one transaction with `FOR UPDATE SKIP LOCKED` (raw SQL), then `StartProcessing`. If none, return `NothingToDo`.
2. Read the file and call the extractor.
3. **Success:** run the flag rules, with "today" as the server's local date, then `CompleteExtraction`. Subtotal and tax are dropped. The result is `Completed`.
4. **Failure** (extractor, gateway or PDF error, missing file, unreadable file, timeout): `FailAttempt(reason, MaxAttempts)`. `LastError` is a fixed or `ExtractionException` reason, so it has no receipt content. The result is `Failed`, with `WillRetry` true when the receipt went back to Queued.
5. **Cancellation on shutdown:** `ReturnToQueue`, no attempt charged, result `Interrupted`. A cancellation that is not the caller's token counts as a failed attempt.
6. Never let one bad receipt throw to the worker. Unexpected errors are logged and count as a failed attempt, and the stored reason is "An unexpected error occurred."
7. The outcome is saved with no cancellation token, so it is recorded even during shutdown. A failure to save is not caught: the exception reaches the worker, and the receipt stays Processing until the next startup recovery returns it to Queued.

`MaxAttempts` starts at 1, which means no retry. Requeued receipts are taken in `CreatedAt` order, and `MaxAttempts` is re-read on every call.

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
- Changing `MaxAttempts` or `FlagTotalLimit` applies to the next call without a restart.
- Invalid settings throw before a receipt is claimed.
- The concurrency criteria are tested against a real PostgreSQL (`tests/Integration.Tests`).
