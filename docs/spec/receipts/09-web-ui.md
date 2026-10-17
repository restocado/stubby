# Web UI

**Status:** Draft  
**Location:** `Web/Components/Pages/`: `Upload.razor`, `Receipts.razor`, `ReviewReceipt.razor`. Blazor Interactive Server. Pages call the slices and hold no business rules.  
**Depends on:** 03 to 08.

## Scope

Pages for upload, the receipts list and review. Monthly totals belong to the reports module. There is no UI design document, so this lists behavior and not layout.

## Behavior

State lives in the database, not the browser. Every page loads current data on open.

- **Upload:** an `InputFile` component that accepts JPG, PNG and PDF, several files at once, camera or photo on a phone. It sends each file to the upload slice separately, over the Blazor circuit, and shows a per-file message for the result: queued, unsupported type, too large, empty, or duplicate with the existing receipt's number, for example "Already uploaded as TXN-000042". A file that was being sent when the connection dropped is not retried; the user selects it again.
- **List:** 25 per page. Shows transaction number, status, flag mark, and merchant, date, total and category where known. Queued and Processing rows have no open or delete action. Delete asks for confirmation. Failed rows have Reprocess. A "Reprocess all failed" action shows the count requeued. The list reloads every 3 seconds, and only while the current page has a Queued or Processing row. `/receipts?page=N` selects the page, with Previous and Next links. With no receipts it links to Upload, and a page past the end links to the last page. The transaction number is a link to the review page for every receipt that can be opened (ForReview and Failed) and for Approved ones, which open read-only. Queued and Processing rows are plain text. The last cell of a row holds its actions: Reprocess on a Failed row, and Delete on every row that is not Queued or Processing. Delete asks "Delete TXN-000042?" in place and needs "Yes, delete". A "Reprocess all failed" button sits above the table. A message shows what happened, and the list reloads.
- **Review:** shows the transaction number as read-only text. Image or PDF beside the fields, stacked on narrow screens. Merchant, date, total and a category select, with the eight categories in lowercase and a "Choose a category" prompt. A flagged receipt shows a banner with reasons. Failed receipts show the last error and offer manual entry, reprocess or delete. Reprocess and a confirmed delete both return to the list. Nothing is saved until Approve, which returns to the list. A problem with a field shows under that field, and a receipt approved in another tab shows a message and no change. Approved receipts are read-only with no approve button. See [05-get-receipt-for-review.md](05-get-receipt-for-review.md) for the details.
- Errors show a short message, never an unhandled error page (NFR-R3).

## Acceptance criteria

- A rejected upload shows an error and adds no row.
- A duplicate upload shows the existing receipt's number in the form `TXN-000042`.
- Choosing 3 valid files creates 3 Queued receipts with increasing transaction numbers and the page stays usable.
- The transaction number appears in the list and on review in the form `TXN-000042`, and cannot be edited.
- A flagged receipt is marked in the list and has a banner on review (NFR-U3).
- Queued and Processing rows cannot be opened or deleted.
- Changing the total and approving stores both the extracted and the final total.
- An Approved receipt has no editable input and no approve button.
- Cancelling the delete confirmation deletes nothing.
- After a dropped connection, reloading shows current server state.
- At 375 px wide nothing overflows sideways and every action is reachable (NFR-U2).
- Works in current Chrome, Edge and Firefox. List and review each load in under 2 seconds.
