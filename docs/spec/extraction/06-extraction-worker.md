# Extraction Worker

**Status:** Draft  
**Location:** `Web/Workers/ExtractionWorker.cs` (`BackgroundService`). `ExtractionOptions` (`MaxAttempts`, `FlagTotalLimit`, `PollInterval`, model, storage path). Startup recovery in `Application/Features/Extraction/RecoverInterruptedReceipts/`: `RecoverInterruptedReceiptsCommand` and handler.  
**Depends on:** 05-process-next-receipt.

## Scope

Run the processing loop in the web app and lose no receipt on restart (NFR-R2).

## Behavior

- **Startup:** before the loop, return every Processing receipt to Queued with `AttemptsUsed` unchanged. Log the count.
- **Loop:** run `ProcessNextReceiptCommand`. If it processed one, run again at once. If not, wait `PollInterval`. Log and continue after an exception. One worker instance.
- **Shutdown:** the cancellation reaches the running command, which requeues without charging an attempt.
- **Settings:** invalid or missing settings fail at startup with a clear message. The API key comes from user-secrets or an environment variable.
- The worker does not pause when the gateway circuit is open (architecture). Receipts use their attempts and become Failed.

## Acceptance criteria

- Receipts Queued before start are processed after start.
- A receipt left Processing by a crash is Queued after start, `AttemptsUsed` unchanged.
- An upload while the worker is busy returns at once and is picked up next.
- An empty queue makes the worker wait, not spin.
- An exception for one receipt does not stop the loop.
- Starting with `MaxAttempts` of 0 or less, or with no storage path or model, fails at startup.
- With the provider down, receipts end Failed with no unhandled error, and "reprocess all failed" recovers them (NFR-R3).
- Median upload-to-saved time is under 60 seconds for a single-page receipt, checked manually (NFR-P1).
