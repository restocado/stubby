# Extraction Worker

**Status:** Built  
**Location:** `Web/Workers/ExtractionWorker.cs` (`BackgroundService`) and `ExtractionWorkerOptions.cs` (`PollInterval`, from `Extraction:PollIntervalSeconds`, 2 to start with). `MaxAttempts` and `FlagTotalLimit` are the `Extraction` settings of slice 05, and the model and storage path are the gateway and image storage settings. Startup recovery in `Application/Features/Extraction/RecoverInterruptedReceipts/`: `RecoverInterruptedReceiptsCommand` and handler, using `IReceiptRepository.RequeueInterruptedAsync`.  
**Depends on:** 05-process-next-receipt.

## Scope

Run the processing loop in the web app and lose no receipt on restart (NFR-R2).

## Behavior

- **Startup:** before the loop, return every Processing receipt to Queued with `AttemptsUsed` unchanged, in one transaction, using the domain's `ReturnToQueue`. Log the count. If the database is not ready, it logs and tries again every `PollInterval` until it works or the app stops, so the worker never starts the loop before recovery.
- **Loop:** run `ProcessNextReceiptCommand`, each in its own DI scope. If it processed a receipt (completed or failed), run again at once. If the queue was empty, wait `PollInterval`. After an unexpected exception, log it and wait `PollInterval` before the next call, so a persistent fault such as a database outage does not become a tight loop. One worker instance.
- **Shutdown:** the cancellation reaches the running command, which requeues without charging an attempt, and the loop ends.
- **Settings:** invalid or missing settings fail at startup with a clear message: `MaxAttempts` below 1, `FlagTotalLimit` of 0 or less, `PollIntervalSeconds` of 0 or less (or over 3600), no image storage path, no model, or a non-https provider URL. The API key is not checked at startup, since the rest of the app runs without it. It comes from user-secrets or an environment variable.
- The worker starts when the app starts, so it begins processing every receipt that is already Queued, using the configured model.
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
