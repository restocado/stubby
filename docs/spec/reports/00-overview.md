# Reports Module : Overview

**Status:** Built

Monthly totals by category and CSV export. Both read Approved receipts only.

Follows `docs/architecture.md`: one query slice each, in `Application/Features/Reports/<Slice>/`. No accuracy screen in v1. Both read through `IReportReader` (`Application/Abstractions/`), implemented in `Infrastructure/Persistence/ReportReader.cs` with exact PostgreSQL sums and no tracking.

| File | Contents |
|---|---|
| [01-get-monthly-totals.md](01-get-monthly-totals.md) | Totals by category for a month |
| [02-export-receipts-csv.md](02-export-receipts-csv.md) | CSV export |
| [05-get-totals-trend.md](05-get-totals-trend.md) | 12-month strip, change on the month before, average |
| [03-test-plan.md](03-test-plan.md) | Cross-cutting tests |
| [04-open-decisions.md](04-open-decisions.md) | Open items |

**Build order:** needs receipts 01, 02 and 06. Then 01 and 02 in parallel.
