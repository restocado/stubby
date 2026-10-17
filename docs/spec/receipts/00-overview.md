# Receipts Module : Overview

**Status:** Draft

User actions on a receipt: upload, list, open, approve, reprocess, delete. Also the domain model and tables behind them.

Follows `docs/architecture.md`: one feature slice per command or query, in `Application/Features/Receipts/<Slice>/` (command or query, handler, validator, response). Dependencies point inward: Web and Infrastructure → Application → Domain. Not started.

| File | Contents |
|---|---|
| [01-domain-model.md](01-domain-model.md) | Entities, enums, transitions, immutability |
| [02-persistence.md](02-persistence.md) | EF Core mapping and migration |
| [03-upload-receipt.md](03-upload-receipt.md) | Upload |
| [04-list-receipts.md](04-list-receipts.md) | Paged list |
| [05-get-receipt-for-review.md](05-get-receipt-for-review.md) | Open a receipt, serve its image |
| [06-approve-receipt.md](06-approve-receipt.md) | Approve |
| [07-reprocess-receipts.md](07-reprocess-receipts.md) | Reprocess one or all Failed |
| [08-delete-receipt.md](08-delete-receipt.md) | Delete |
| [09-web-ui.md](09-web-ui.md) | Blazor pages |
| [10-test-plan.md](10-test-plan.md) | Cross-cutting tests |
| [11-open-decisions.md](11-open-decisions.md) | Open items |

**Build order:** 01 and 02 first; then 03 and 04; then 05 to 08 in any order; 09 alongside each slice.
