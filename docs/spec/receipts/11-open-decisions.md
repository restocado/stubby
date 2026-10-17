# Receipts Module : Open Decisions

**Status:** Draft

None are decided until the author confirms them.

| # | Question | Blocks | Recommendation |
|---|---|---|---|
| 1 | How is file type checked: content type, extension or file signature? | 03 | Check all three and reject if any disagree. |
| 3 | May the user approve a total of zero or less, or a future or very old date? | 01, 06 | Yes. The flag is advice. Block only missing values. |
| 4 | Must a manual entry fill all three fields? | 01, 06 | Yes. The database requires it for Approved rows. |
| 5 | How does review show a multi-page PDF? | 05, 09 | Embed the PDF with a link to open it in a new tab. |
| 6 | Is a blank extracted value stored as null or empty text? | 06 | Null. |
| 7 | Does the list refresh by itself while receipts process? | 04, 09 | Yes, every few seconds, only while something is Queued or Processing. |
| 8 | Is a concurrency token needed for approve vs delete in two tabs? | 02, 06, 08 | No. Re-check status inside the transaction. |
