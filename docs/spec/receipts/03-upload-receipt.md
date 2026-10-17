# Upload Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/UploadReceipt/`: `UploadReceiptCommand`, `UploadReceiptCommandHandler`, `UploadReceiptCommandValidator`, `UploadReceiptResponse`. Uses `IImageStore`. Called by `Upload.razor` through an `InputFile` component (see 09-web-ui). There is no HTTP endpoint for upload.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Validate a file, reject duplicates, store it, queue the receipt and return at once (NFR-P2).

The command is a Mediator `ICommand<UploadReceiptResponse>`, handled by an `ICommandHandler`, and the page sends it through `IMediator`. It carries the file name, content type, declared size and a read stream. The response is one of: `Created(id, transactionNo)`, `UnsupportedType`, `TooLarge`, `Empty` or `Duplicate(existingTransactionNo)`. Rejections are returned values, not exceptions. The slice knows nothing about HTTP; the page turns each case into a message.

## Behavior

1. Reject any type other than JPG, PNG or PDF (NFR-U4).
2. Reject files that are empty or over 10 MB (10 485 760 bytes). No page limit for PDFs. The page opens the stream with a cap of 10 485 761 bytes, so an oversized file reaches the handler and comes back as `TooLarge` instead of throwing. The handler reads at most that many bytes and does not trust the declared size.
3. Compute the SHA-256 checksum (lowercase hex). Reject if a receipt with that checksum exists in any status, returning that receipt's transaction number.
4. Store the file, create the receipt as Queued, save, return its id and the transaction number the database assigned.
5. If saving fails, delete the stored file. A unique-index violation from a race counts as a duplicate: look up the existing receipt by checksum and return `Duplicate` with its number.

Rejections queue nothing and leave no file. `InputFile` sends the file over the Blazor circuit in segments, so the SignalR message size limit is left at its default. This is unverified: upload a 10 MB file in the running app, and raise `MaximumReceiveMessageSize` only if it fails.

## Acceptance criteria

- A valid JPG, PNG and PDF each return `Created` and create one Queued receipt.
- A `.txt` or `.gif` returns `UnsupportedType`, with no row and no file.
- A file of 10 485 760 bytes is accepted, 10 485 761 returns `TooLarge`, an empty file returns `Empty`.
- The same bytes uploaded twice return `Duplicate` the second time, with one row and one file. This holds when the first is Approved, ForReview, Failed, Queued or Processing.
- `Duplicate` carries the `transactionNo` of the existing receipt, not a new number.
- After the first is deleted, the same bytes return `Created`.
- Two identical simultaneous uploads give one `Created` and one `Duplicate`, and the `Duplicate` names the number from the `Created`.
- Each accepted upload gets a `TransactionNo` higher than the one before.
- Two simultaneous uploads get different numbers.
- Rejected uploads (type, size, duplicate) do not use up a new number.
- The response does not wait for extraction.
- A save failure removes the stored file.
