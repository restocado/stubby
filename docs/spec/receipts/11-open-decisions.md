# Receipts Module : Open Decisions

**Status:** Closed

Nothing is open now. Each question was answered with its recommendation, built that way and merged, and is written into the spec it belongs to:

| # | Question | Answer | Where |
|---|---|---|---|
| 1 | How is file type checked? | Content type, extension and file signature must all agree, or the file is rejected. | [03-upload-receipt.md](03-upload-receipt.md) |
| 3 | May the user approve a total of zero or less, or a future or very old date? | Yes. The flag is advice. Only a missing or malformed value blocks an approval. | [06-approve-receipt.md](06-approve-receipt.md) |
| 4 | Must a manual entry fill all three fields? | Yes. The database requires it for Approved rows. | [06-approve-receipt.md](06-approve-receipt.md) |
| 5 | How does review show a multi-page PDF? | The PDF is embedded in an `<iframe>`, with a link to open it in a new tab. | [05-get-receipt-for-review.md](05-get-receipt-for-review.md) |
| 6 | Is a blank extracted value stored as null or empty text? | Null. | [06-approve-receipt.md](06-approve-receipt.md) |
| 7 | Does the list refresh by itself while receipts process? | Yes, every 3 seconds, and only while the page has a Queued or Processing row. | [09-web-ui.md](09-web-ui.md) |
| 8 | Is a concurrency token needed for approve vs delete in two tabs? | No. The row is locked and the status re-checked inside the transaction. | [06-approve-receipt.md](06-approve-receipt.md) |

(#2 was dropped earlier.)
