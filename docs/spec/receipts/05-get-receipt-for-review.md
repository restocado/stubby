# Get Receipt For Review

**Status:** Built  
**Location:** `Application/Features/Receipts/GetReceiptForReview/`: `GetReceiptForReviewQuery`, `GetReceiptForReviewQueryHandler`, `GetReceiptForReviewResponse`. And `Application/Features/Receipts/GetReceiptImage/`: `GetReceiptImageQuery`, `GetReceiptImageQueryHandler`, `GetReceiptImageResponse`. Both are Mediator queries that use `IReceiptRepository.FindByIdAsync`, and the image one also `IImageStore.ReadAsync`. Image endpoint in Web (`Web/Endpoints/ReceiptEndpoints.cs`): `GET /api/receipts/{id:guid}/image`. The review page is `Web/Components/Pages/ReviewReceipt.razor` at `/receipts/{id}`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

Return what the review screen needs, and serve the receipt file.

## Behavior

- Both queries return a result value: `NotFound` for an unknown id, `Locked` for Queued or Processing, and `Found` for ForReview, Failed and Approved. The query slices know nothing about HTTP. Only the image endpoint turns them into 404, 409 and 200.
- Response: `Id`, `TransactionNo`, `Status`, `OriginalFileName`, `ContentType`, `IsFlagged`, `FlagReasons` (a list of `FlagReason`, with any name this version does not know skipped), extracted merchant, date and total. Approved also returns final values and category. Failed also returns `LastError`.
- The image endpoint sends the stored file inline with its stored content type, `X-Content-Type-Options: nosniff` and `Cache-Control: private, no-cache`. Same locking rules. It looks up `ImagePath` by id and never takes a path from the caller. A missing file, or a stored path the image store refuses because it is outside the storage root, is `NotFound`, and is logged.
- **Review page:** see [09-web-ui.md](09-web-ui.md). It shows a back link above everything (to the inbox, or to the list for an Approved receipt), a heading with the transaction number, the image (or, for a PDF, an `<iframe>` with an "Open the PDF in a new tab" link), and the form. A ForReview receipt starts with the extracted values and no category. A Failed receipt starts with its last error and two choices, and opens an empty form as a manual entry only when one is chosen. A flagged receipt shows a banner that gives each reason in words. An Approved receipt shows its final values and category as text, with no inputs and no approve button. A receipt that is still being read, and an unknown one, each show a short message with a link back.

## Acceptance criteria

- Every returned receipt includes its `TransactionNo`, and the review screen shows it as `TXN-000042`.
- ForReview returns extracted fields, flag and reasons.
- Failed returns null extracted fields and `LastError`.
- Approved returns final values and the UI shows no edit controls.
- Queued and Processing return `Locked` for both the query and the image, and the image endpoint answers 409.
- An unknown id returns `NotFound`, and the image endpoint answers 404. So does anything in the route that is not a GUID.
- The image bytes equal the uploaded bytes, with the right content type, for a PNG and a PDF.
- No parameter accepts a path, and a stored path outside the storage root is never served.
- Both load in under 2 seconds for a single-page receipt (NFR-P4).
