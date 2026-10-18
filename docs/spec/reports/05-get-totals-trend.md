# Get Totals Trend

**Status:** Built  
**Location:** `Application/Features/Reports/GetTotalsTrend/`: `GetTotalsTrendQuery`, `GetTotalsTrendQueryHandler`, `GetTotalsTrendQueryValidator`, `GetTotalsTrendResponse`. A Mediator query. Uses `IReportReader.GetMonthlySumsAsync`.  
**Depends on:** 01-get-monthly-totals.

## Scope

The approved total of each of 12 months, how the last compares with the month before, and an average. It feeds the strip and the comparison on the totals page.

## Behavior

- Input: `Year` (1 to 9998) and `Month` (1 to 12), the last month of the window. Anything else makes the handler throw FluentValidation's `ValidationException`.
- `Months`: 12 entries, oldest first, the last being the month asked for. A month with no approved receipts is 0.00. Only Approved receipts count, by the date on the receipt, as in the monthly totals. Months before year 1 do not exist, and are 0.00 without being read.
- `PreviousMonthTotal`: the month before the last, 0.00 when it has nothing.
- `ChangePercent`: how far the last month is above (positive) or below (negative) the one before, in percent, rounded to one decimal. Null when the month before has nothing to compare with.
- `AveragePerMonth`: the average of the months that have approved receipts, rounded to two decimals, 0.00 when none do. `MonthsWithReceipts` says how many that is. A month with no receipts does not pull the average down.

## Acceptance criteria

- The window is 12 months, oldest first, and ends with the month asked for, including across a new year.
- A month with no receipts is 0.00.
- 256.80 then 288.42 gives a change of 12.3. A fall gives a negative change. Nothing in the month before gives no change.
- The average is over the months that have receipts only, and is 0.00 with none.
- The first year does not ask for months that do not exist.
- A month or year that does not exist fails validation.
