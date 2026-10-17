# Get Monthly Totals

**Status:** Draft  
**Location:** `Application/Features/Reports/GetMonthlyTotals/`: `GetMonthlyTotalsQuery`, `GetMonthlyTotalsQueryHandler`, `GetMonthlyTotalsQueryValidator`, `GetMonthlyTotalsResponse`. Page: `Web/Components/Pages/MonthlyTotals.razor`.  
**Depends on:** receipts 01 and 02.

## Scope

Show where one month's money went, by category.

## Behavior

- Input: `Year`, `Month`. The page defaults to the current month and has previous and next buttons.
- Counts Approved receipts only, by the month of their final `ReceiptDate`.
- Output: sum and count for each of the eight categories, always all eight in enum order (0.00 when empty), plus a grand total and count.
- Sums are exact decimals. Amounts are shown with two decimals and no currency symbol.

## Acceptance criteria

- Two Approved Groceries receipts of 10.10 and 20.20 give 30.30 and count 2.
- ForReview, Failed, Queued and Processing receipts add nothing.
- 2026-09-30 is not in October. 2026-10-01 and 2026-10-31 are.
- A receipt uploaded in October but dated in September counts in September.
- An empty month shows eight rows at 0.00 and a grand total of 0.00.
- The grand total equals the sum of the categories. 0.10 summed three times gives exactly 0.30.
- A deleted Approved receipt leaves the totals.
- A month outside 1 to 12 fails validation.
- The page works at 375 px wide and loads in under 2 seconds.
