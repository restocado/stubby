# Get Monthly Totals

**Status:** Built  
**Location:** `Application/Features/Reports/GetMonthlyTotals/`: `GetMonthlyTotalsQuery`, `GetMonthlyTotalsQueryHandler`, `GetMonthlyTotalsQueryValidator`, `GetMonthlyTotalsResponse`. A Mediator query that uses `IReportReader.GetMonthlyTotalsAsync`. Page: `Web/Components/Pages/MonthlyTotals.razor` at `/totals`, with a "Totals" link in the menu.  
**Depends on:** receipts 01 and 02.

## Scope

Show where one month's money went, by category.

## Behavior

- Input: `Year` (1 to 9998), `Month` (1 to 12). An invalid value makes the handler throw FluentValidation's `ValidationException`, as the list slice does. The page never sends one.
- The page is `/totals?year=2026&month=10`. It defaults to the current month, and a missing or impossible value (`abc`, `13`, empty) shows the current month. Query values are read as text and parsed in the page, because binding straight to a number throws on text such as `abc` and gives an error page.
- Counts Approved receipts only, by the month of their final `ReceiptDate`: from the first of the month up to, but not including, the first of the next month.
- Output: sum and count for each of the eight categories, always all eight in enum order (0.00 when empty), plus a grand total and count. The response also carries the first and last month that has an Approved receipt.
- Sums are exact decimals, summed by PostgreSQL on `numeric`. Amounts are shown with two decimals and no currency symbol, because no currency is stored. Categories are shown in lowercase.
- **Previous and next** are links that stay between the first approved month and the current month. A receipt dated after the current month, which approval allows, extends the range to its month. Outside the range the button is shown disabled. With no approved receipts both are disabled.
- Links on the page download the CSV for the month shown, and for every approved receipt.

## Acceptance criteria

- Two Approved Groceries receipts of 10.10 and 20.20 give 30.30 and count 2.
- ForReview, Failed, Queued and Processing receipts add nothing.
- 2026-09-30 is not in October. 2026-10-01 and 2026-10-31 are.
- A receipt uploaded in October but dated in September counts in September.
- An empty month shows eight rows at 0.00 and a grand total of 0.00.
- The grand total equals the sum of the categories. 0.10 summed three times gives exactly 0.30.
- A deleted Approved receipt leaves the totals.
- A month outside 1 to 12, and a year outside 1 to 9998, fail validation.
- Nonsense in the query string (`year=abc`, `month=13`, empty, huge or negative numbers) gives the current month and never an error page. The same holds for `?page=` on the receipts list.
- Loads in under 2 seconds with 1 000 approved receipts. The 375 px layout uses a responsive table and has not been checked on a phone.
