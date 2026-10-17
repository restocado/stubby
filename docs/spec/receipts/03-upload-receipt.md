# Upload Receipt

**Status:** Draft  
**Location:** `Application/Features/Receipts/UploadReceipt/`: `UploadReceiptCommand`, `UploadReceiptCommandHandler`, `UploadReceiptCommandValidator`, `UploadReceiptResponse`. Uses `IImageStore`. Endpoint in Web: `POST /api/receipts` (multipart, one file).  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Validate a file, reject duplicates, store it, queue the receipt and return at once (NFR-P2).

## Behavior

1. Reject any type other than JPG, PNG or PDF (NFR-U4).
2. Reject files that are empty or over 10 MB (10 485 760 bytes). No page limit for PDFs.
3. Compute the SHA-256 checksum (lowercase hex). Reject if a receipt with that checksum exists in any status, returning that receipt's transaction number.
4. Store the file, create the receipt as Queued, save, return its id and the transaction number the database assigned.
5. If saving fails, delete the stored file. A unique-index violation from a race counts as a duplicate: look up the existing receipt by checksum and return its number.

Proposed responses (not in the source docs): 201 `{ "id", "transactionNo" }`, 400 for type or size, 409 for duplicate, with the existing receipt's `transactionNo` in the body. Rejections queue nothing and leave no file.

## Acceptance criteria

- A valid JPG, PNG and PDF each return 201 and create one Queued receipt.
- A `.txt` or `.gif` returns 400, with no row and no file.
- A file of 10 485 760 bytes is accepted, 10 485 761 is rejected, an empty file is rejected.
- The same bytes uploaded twice return 409 the second time, with one row and one file. This holds when the first is Approved, ForReview, Failed, Queued or Processing.
- The 409 body carries the `transactionNo` of the existing receipt, not a new number.
- After the first is deleted, the same bytes return 201.
- Two identical simultaneous uploads give one 201 and one 409, and the 409 names the number from the 201.
- Each accepted upload gets a `TransactionNo` higher than the one before.
- Two simultaneous uploads get different numbers.
- Rejected uploads (type, size, duplicate) do not use up a new number.
- The response does not wait for extraction.
- A save failure removes the stored file.
