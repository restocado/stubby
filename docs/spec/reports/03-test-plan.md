# Reports Module : Test Plan

**Status:** Built

Scenarios across receipts and reports. Tests use a separate test database.

1. Approve receipts across three categories and two months. Each month's totals match sums computed independently.
2. The CSV `Total` sum for a month equals the grand total on the totals page.
3. A receipt is in neither report until Approved. After approval it is in both. After delete it is in neither, and its field records remain.
4. A manually entered (Failed) receipt counts in both reports like any other.
5. 1 000 receipts of amounts such as 0.01, 0.10 and 19.99 sum to the exact expected decimal.
6. The largest `numeric(15,2)` amount round-trips through totals and CSV.
7. Open the CSV in Excel by double-click with a merchant containing a comma, a quote, an accent and a leading `=`. All display correctly (manual).
8. With 1 000 Approved receipts the totals page loads in under 2 seconds and the export completes.
