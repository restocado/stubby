# Get Receipt For Review

**Status:** Draft  
**Location:** `Application/Features/Receipts/GetReceiptForReview/`: `GetReceiptForReviewQuery`, `GetReceiptForReviewQueryHandler`, `GetReceiptForReviewResponse`. And `Application/Features/Receipts/GetReceiptImage/`: `GetReceiptImageQuery`, `GetReceiptImageQueryHandler`. Image endpoint in Web: `GET /api/receipts/{id}/image`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Return what the review screen needs, and serve the receipt file.

## Behavior

- Unknown id: not found. Queued or Processing: conflict (locked). ForReview, Failed and Approved return data.
- Response: `Id`, `TransactionNo`, `Status`, `OriginalFileName`, `ContentType`, `IsFlagged`, `FlagReasons` (list), extracted merchant, date and total. Approved also returns final values and category. Failed also returns `LastError`.
- The image endpoint streams the stored file inline with its content type and `X-Content-Type-Options: nosniff`. Same locking rules. It looks up `ImagePath` by id and never takes a path from the caller.

## Acceptance criteria

- Every returned receipt includes its `TransactionNo`, and the review screen shows it as `TXN-000042`.
- ForReview returns extracted fields, flag and reasons.
- Failed returns null extracted fields and `LastError`.
- Approved returns final values and the UI shows no edit controls.
- Queued and Processing return conflict, for both the query and the image.
- Unknown id returns not found.
- The image bytes equal the uploaded bytes, with the right content type.
- No parameter accepts a path, and a stored path outside the storage root is never served.
- Both load in under 2 seconds for a single-page receipt (NFR-P4).
