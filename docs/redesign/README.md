# Stubby : Redesign Starting Point

Date: 2026-10-18  
Author: restocado

Screenshots of the app as it is, an audit of what they show, and low-fidelity wireframes and high-fidelity mockups for a redesign. Nothing here changes the app. It is the material to decide from.

| File | What it is |
|---|---|
| [`screenshots/`](screenshots/) | 12 desktop pages (1280 px wide) and 6 phone pages (390 px wide) of the running app |
| [`wireframes.html`](wireframes.html) | Grey-box wireframes of 6 screens plus the small parts, each on desktop and on a phone. Open it in a browser. |
| [`wireframes.png`](wireframes.png) | The same page as one long picture |
| [`mockups.html`](mockups.html) | High-fidelity mockups of the same screens: desktop, phone, small parts and a style guide. Open it in a browser. |
| [`mockups.png`](mockups.png) | The same page as one long picture |

The screenshots show invented receipts only (NFR-D4). They were taken from a copy of the app running on a throwaway database, with its background worker idle, so nothing was sent to the model provider. The twelve receipts cover every status: five approved, four for review (two of them flagged, one a PDF), one failed, one queued and one processing.

## What the screenshots show

**Home (`01-home`).** It is still the template's "Hello, world! Welcome to your new app." The first page anyone sees says nothing about receipts. The sidebar says "Stubby.Web", the top bar has an "About" link to Microsoft's ASP.NET documentation, and the purple gradient is the template's.

**Navigation.** Receipts and Totals use the same icon. Upload, the thing you do most, is last in the list and does not appear anywhere else: the list has no way to add a receipt. On a phone the menu collapses behind a button, and "About" takes a whole row.

**Upload (`02-upload`).** A single bare file input under one line of text. There is no camera shortcut for a phone, no drop target, and after the files are in no link to what happens next, so the page is a dead end.

**Receipts list (`03-receipts-list`, `m03-receipts-list`).**
- The first columns are the transaction number and the file name. Neither is what you recognise a receipt by. The merchant, date and total are further right, and the file name wraps over two or three lines.
- Every row repeats a Delete button, and Failed rows add Reprocess. "Reprocess all failed" is always in the corner, even when nothing has failed.
- There is no search, no filter and no thumbnail. Status and a flag take two badges in one cell. Totals have no thousands separator (`3697.54`).
- **On a phone the list does not work.** At 390 px the file name wraps to one or two characters a line, and the merchant, date, total, category and actions are off the right edge. Twelve receipts make a page 2,500 px tall.

**Review (`04` to `09`, `m04`, `m06`).**
- The side-by-side layout is right: the receipt on the left, the fields on the right, stacked on a phone.
- But the picture is small and cannot be zoomed or rotated, which matters most for the blurry and tilted photos that most need a close look. The lower half of the desktop page is empty.
- Category is a dropdown, so two taps. The date shows `09/14/2026`. The total shows `3697.54`.
- Approving returns to the list. Reviewing a pile of receipts means list, open, approve, list, open, approve.
- A Failed receipt (`08`) shows an error and then four empty fields with an Approve button, which reads as a form you are expected to fill in. The Reprocess and Delete buttons sit side by side.

**Totals (`10`, `11`).** A plain table of eight categories whatever the month has, and most months are mostly zeros (`11` is all zeros). The Previous and Next buttons are small, with no way to jump to a month. A number cannot be traced back to its receipts. The CSV links are text links under the table.

**Everywhere.** No feedback when something is approved or deleted (the page just changes). The not-found page (`12`) is a plain sentence and a link back, which is fine, but it looks like every other page and says nothing about what to do next. Empty states are a sentence, not a next step.

## What to keep

These are in the app because of the rules, so the redesign has to respect them.

- An **approved receipt can never be edited**, only deleted (NFR-R4). So there is no "undo approve".
- **Queued and Processing receipts are locked**: they cannot be opened or deleted. They can only be shown as "being read".
- **Reprocess is for Failed receipts only.**
- **Deleting keeps the anonymous field records** (NFR-D2), and a deleted receipt's number is never reused.
- A **manual entry fills all three fields** (merchant, date and total).
- **Subtotal and tax are never stored or shown.** They only feed the flag check.
- **No currency is stored**, so no currency symbol is shown.
- A receipt is never lost and the app never shows an unhandled error (NFR-R2, NFR-R3).
- **Fake data only** in the repository, screenshots included (NFR-D4).

## Direction

Stubby has one person and one loop: **add receipts, check what was read, approve, see where the money went.** The wireframes are built around that loop, and around the two places it happens: a phone in your hand when you photograph a receipt, and a desk when you check them.

1. **Open on the work, not on a menu.** The home page becomes an Inbox: what needs review, what failed, what is still being read, and this month's total.
2. **Recognise a receipt the way you do.** The merchant, the date and the amount lead everywhere, with no thumbnails (decided). The transaction number and the file name are small and secondary.
3. **Make the loop one tap at a time.** "Add receipts" is always in reach. Review ends in "Approve & next", so a pile is a loop, with no trip back to a list.
4. **Give the receipt room.** The picture is large, with zoom, rotate and fit.
5. **Say what happened in words, and what to do next.** A flag says why. A failure says whether the receipt is fine, and offers "Try reading again" first.
6. **Desktop leads, and phones still work.** Decided: receipts are mostly added and checked at a desk. So the desktop layouts are the detailed ones, with room for the picture and keyboard shortcuts for the review loop. Phones get cards and not a table, a bottom tab bar with a raised Add button, Approve fixed to the bottom edge and a camera button, so that adding a receipt on the go works. Today's phone list is broken (see above), so this is a fix, not a feature.
7. **Keep the risky things behind a menu and a confirmation**, with the consequence written out.

**Navigation:** Inbox, Receipts, Totals, and an "Add receipts" button. A left rail on desktop and a bottom tab bar on a phone. The Inbox shows how many receipts are waiting. The "Receipts" label stays as it is today (decided).

## What each wireframe needs

Some of this is only a different layout of what exists. Some needs new work behind it.

| Screen | Only layout | Needs something new |
|---|---|---|
| Inbox | Cards, stat tiles, "Try again" and "Type it in" (reprocess exists) | Counts by status; "approved this month" (reuses the totals query) |
| Add receipts | Per-file rows; "Go to inbox" | A camera-first button on phones; a real drop zone (the file input only accepts a drop on itself) |
| All receipts | Row and card layouts; chips; the row menu | Search by merchant; filter by status and month; a count of all matching receipts |
| Review | Category chips, date and total formats, the stuck-to-the-bottom Approve, "Show what was read" (the extracted values are already stored) | **Approve & next** and "1 of 3": a query for the receipts to review in `CreatedAt` order, oldest first, which is the order the worker reads them, and a way to skip one to the end of the round; keyboard shortcuts (off while a field has focus); a zoom, rotate and fit viewer, which is client-side work and different for a PDF |
| Could not be read | Hiding the form until "Type it in myself" | Nothing |
| Totals | Bars, hiding empty categories, a month picker | Comparison with last month; a 12-month strip; a link from a category to the receipts behind it (needs the category filter above) |
| Small parts | Row menu, delete confirmation, empty state, approval message | Nothing |

## Decided

- **Desktop leads.** It is used mostly at a desk. Phones still have to work.
- **The "Receipts" label stays.** The Inbox is a new page beside it, not a rename of it.
- **"Approve & next" goes oldest first**, the order the receipts were added and read, with "1 of 3" showing the place. A receipt can be skipped.
- **No thumbnails.** A row is recognised by merchant, date and amount, and a PDF carries a small PDF tag. The wireframes still show thumbnail boxes because they were drawn first; the mockups follow this decision. Nothing needs to be generated or stored for it.
- **The look** is the one in the mockups, drawn from scratch in plain CSS. Bootstrap is gone and no other framework was added.
- **The name "Inbox"** for the home page.
- **Totals extras** (the comparison with the month before, the 12-month strip and the average month) are in.

## Built

The redesign is built. See `docs/spec/receipts/09-web-ui.md` for how each page behaves, `12-inbox-and-review-queue.md` and `docs/spec/reports/05-get-totals-trend.md` for the new queries, and `04-list-receipts.md` for the filters. Where the build differs from the mockups:

- **"1 of 3" is a place in what is waiting now.** After an approval the next receipt shows "1 of 2", not "2 of 3". Nothing is kept between receipts, so a receipt that arrives or is approved in another tab is taken into account.
- **Skip opens the next receipt** in the same order, and after the newest it goes round to the oldest. It does not move the receipt to the end of a round, because there is no stored round.
- **The end of a round is the Inbox.** After the last approval the page says what was approved and "You're all caught up". There is no "3 approved, none skipped" card, because nothing counts a round.
- **The Inbox shows no "around two minutes"** and no Enter hint on "Start reviewing": the first was a guess and the second is only a shortcut on the review page.
- **Category chips on Totals link to the list** (status Approved, that category and month). "Show them" on Totals adds the empty categories.
- **Reprocess all failed** is "Try all again", on the list when it shows the Failed receipts.
- **The phone tab bar has four tabs**, Inbox, Receipts, Add and Totals, so the raised Add button is third and not centred.

## Still open

1. **Anything missing?** For example editing an approved receipt's category is impossible today. If you want that, it is a rule change, not a layout one.

## How these were made

Screenshots: `dotnet run` against a database made for the purpose, with seeded receipts, then headless Edge. The phone shots use a 390 px viewport set through Edge's remote debugging protocol, because headless Edge will not make a window narrower than 500 px and the first attempt only cropped a wider layout. The wireframes and the mockups are plain HTML and CSS with a few lines of script (the mockups use it for the side rail and the tab bar), no libraries. The mockups show the invented receipts from `testset/`, and the figures on them (including the 12-month chart) are made up.
