# Reports Module : Open Decisions

**Status:** Draft

Nothing is open now. Each question was answered with its recommendation, with one small extension, built that way and written into the spec it belongs to:

| # | Question | Answer | Where |
|---|---|---|---|
| 1 | Is a currency symbol shown? | No. No currency is stored. | [01-get-monthly-totals.md](01-get-monthly-totals.md) |
| 2 | Which months can be picked? | From the earliest Approved receipt to the current month. A receipt dated after the current month, which approval allows, extends the range to its month, so that it is not out of reach. | [01-get-monthly-totals.md](01-get-monthly-totals.md) |
| 3 | Does the CSV export all Approved receipts or one month? | All, with an optional month filter (`year` and `month` together). A month's file is named for the month. | [02-export-receipts-csv.md](02-export-receipts-csv.md) |
| 4 | Which columns does the CSV have? | `TransactionNo` (as `TXN-000042`), `Date`, `Merchant`, `Category` (in lowercase), `Total`. | [02-export-receipts-csv.md](02-export-receipts-csv.md) |
