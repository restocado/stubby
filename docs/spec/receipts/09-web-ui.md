# Web UI

**Status:** Built  
**Location:** `Web/Components/`: `Pages/Home.razor` (the inbox), `Upload.razor`, `Receipts.razor`, `ReviewReceipt.razor`, `MonthlyTotals.razor`; `Layout/` (`MainLayout`, `NavMenu`, `FocusLayout`); `Shared/` (`Icon`, `StatusChip`, `Toast`, `ApprovedToast`, `ConfirmDialog`). Blazor Interactive Server, except the totals page, which needs no circuit. Pages call the slices and hold no business rules. The look is plain CSS: tokens and shared pieces in `wwwroot/app.css`, the rest beside each page. There is no CSS framework.  
**Depends on:** 03 to 08, and 12 for the inbox and the review queue.

## Scope

The pages for the inbox, adding receipts, the list, review and the totals. The mockups in `docs/redesign/mockups.html` show the intended look. Desktop leads, because receipts are mostly added and checked at a desk, and every page also works at 375 px.

## Behavior

State lives in the database, not the browser. Every page loads current data on open. A bad value in an address is the same as none, so a hand-edited link never gives an error page (NFR-R3).

- **Menu:** a left rail on a desktop and a bottom tab bar on a phone, with Inbox, Receipts, Totals and "Add receipts" (a raised button on a phone). Inbox shows the number of receipts waiting for review, refreshed every 5 seconds and on every page change. A review runs without the menu (`FocusLayout`), so the receipt gets the room.
- **Inbox (`/`):** how many receipts wait for review, could not be read and are being read; the receipts to review oldest first, each with what was read, a short flag reason ("Total looks large", "Date missing", and "+2" for more) and a Review button; the receipts that could not be read, each with "Try again" (reprocess) and "Type it in"; a line for the receipts being read; "Start reviewing" opens the oldest; and this month's approved total with its three biggest categories. Nothing to do shows "You're all caught up". It reloads every 3 seconds while a receipt is being read. A made-up or not-approved `approved` value in the address shows no message.
- **Add receipts (`/upload`):** the file input covers the whole drop zone, so a click or a drop anywhere works. It accepts JPG, PNG and PDF, several at once. On a phone a second button opens the camera (`capture`). Each file is sent separately over the circuit and gets a row: Added, Already there (with the existing number, for example "Already added as TXN-000042"), or Skipped with the reason (unsupported type, too large, empty). A file that was being sent when the connection dropped is not retried. A summary line ("2 added · 1 already there · 1 skipped") and "Go to inbox" show when the batch is done.
- **Receipts (`/receipts`):** 25 per page, newest first. Search by merchant or file name (after a short pause), a status tab (All, To review, Approved, Couldn't be read) and a month (All months, or any month from the first approved receipt to the current one). A category chip shows when a category is chosen, and removes it. All of it is in the address (`search`, `status`, `month`, `category`, `page`), so a link from the inbox or the totals page lands on the right list. A row shows merchant (or the file name), number, date, category, a status chip, and total, with no thumbnail. A flagged ForReview receipt says "Check this". Queued and Processing rows are plain text with no menu. Every other row has an actions menu: Open, "Try reading again" (Failed only) and Delete. Delete opens a confirmation that says what goes ("The receipt and its picture are removed for good, and totals stop counting it. The record of what was read stays, with nothing linking it back. This can't be undone."), with "Keep it" as the focused answer. On a Failed filter a "Try all again" button requeues every failed receipt. A message says what happened and the list reloads. It reloads every 3 seconds while the page has a Queued or Processing row. On a phone each row is a card. With no receipts it links to Add receipts. With no match it offers "Clear filters". A page past the end links to the last page.
- **Review (`/receipts/{id}`):** the receipt on the left and the form on the right, stacked on a phone. A progress line says "Reviewing 2 of 4, oldest first" for a ForReview receipt.
  - **Picture:** an image can be zoomed (25% steps, 50% to 400%), rotated and fitted, with the buttons or the keys `+`, `-`, `R` and `F`. This happens in the browser. A PDF is shown with the browser's own viewer and a link to open it in a new tab. On a phone the picture is small, with "Open full size".
  - **Form:** merchant, date, a large total, and the eight categories as chips. A flagged receipt has a banner with the reasons, and the fields that caused it are outlined. A ForReview receipt starts with what was read and no category. "Show what was read" lists the values as read.
  - **Approve & next** (`Enter`) approves and opens the next receipt in the review queue (spec 12), and the page says what was approved. "Approve only" approves and goes to the inbox. "Skip" (`S`) opens the next one without approving. With no other receipt waiting, the button is "Approve" and goes to the inbox. On a phone the buttons are fixed to the bottom edge.
  - **Keys:** `Enter`, `S`, `+`, `-`, `R`, `F` and `Esc` (back) are off while a field, a menu button or a question has the focus, so typing never triggers them.
  - **Failed:** shows the last error and two choices, "Try reading it again" (requeues and goes to the inbox) and "Type it in myself" (opens an empty form, as a manual entry; `?type=1` opens it straight away), and a "Delete this receipt" link.
  - **Approved:** read-only values, no approve button and no reprocess, with "Delete…" behind a confirmation. It goes back to the list.
  - A problem with a field shows under that field. A receipt approved in another tab shows a message and no change. A receipt still being read, and an unknown one, each show a short message with a link back.
- **Totals (`/totals`):** see the reports module. The page has a month switcher, the month's total with the change on the month before, the categories that have receipts (each linking to its receipts in the list; "Show them" adds the empty ones), a 12-month strip where each bar is a link, the average month, and the CSV links.
- Errors show a short message, never an unhandled error page (NFR-R3).

## Acceptance criteria

- A rejected upload shows a Skipped row and adds no receipt.
- A duplicate upload shows the existing receipt's number in the form `TXN-000042`.
- Choosing 3 valid files creates 3 Queued receipts with increasing transaction numbers and the page stays usable.
- The transaction number appears in the list and on review in the form `TXN-000042`, and cannot be edited.
- A flagged receipt is marked in the list and has a banner on review (NFR-U3).
- Queued and Processing rows cannot be opened or deleted, and have no menu.
- The inbox lists what needs attention, oldest first, and the menu number matches it.
- Search, status, month and category narrow the list, come from the address, and a bad value in the address is ignored.
- Approve & next opens the next receipt in the queue and says what was approved. Skip goes round after the newest to the oldest.
- The shortcut keys do nothing while a field has the focus.
- Changing the total and approving stores both the extracted and the final total.
- An Approved receipt has no editable input, no approve button and no reprocess.
- Cancelling the delete confirmation deletes nothing.
- After a dropped connection, reloading shows current server state.
- At 375 px wide nothing overflows sideways and every action is reachable (NFR-U2).
- Works in current Chrome, Edge and Firefox. List and review each load in under 2 seconds.
