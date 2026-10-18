# Stubby

Single-user .NET 10 Blazor app that reads receipts with a vision model. See [README.md](README.md) for the overview.

## Status

Complete. Everything in the plan is built and merged into `main`:

- **Upload and reading:** an uploaded receipt is queued. The extraction worker starts with the app and reads every Queued receipt with the configured model, and the receipt ends For review, Failed or flagged.
- **Review:** `/receipts/{id}` shows the receipt beside its fields. Approve saves the final values and the field records. Reprocess and delete are on the list and on the review page of a Failed receipt.
- **Reports:** `/totals` shows a month's approved totals by category, and `/api/reports/receipts.csv` downloads the approved receipts for Excel.
- **UI:** an Inbox home page, a filterable list, a review screen with Approve & next and a zoom/rotate viewer, and totals with a 12-month strip, in plain CSS with no framework (`docs/spec/receipts/09-web-ui.md`; `docs/redesign/README.md` is the audit from before the redesign).
- **Eval tool:** `dotnet run --project src/Eval -- generate` writes the 30 invented receipts and `answers.json` to `testset/` (committed, and not to be edited by hand). `dotnet run --project src/Eval -- run` reads that set through the real extractor with the app's model and secrets, prints a scored report and writes `eval-report.md`. It sends the receipts to the model provider, so run it on purpose.

It has run against the real model: on 2026-10-18 `gemma4:31b` with prompt v1 read all 30 eval receipts with every field right, so every accuracy target passes (`eval-report.md`, `docs/extraction-prompt.md`). The set is invented and clean, so that does not promise the same on real receipts.

Build with `dotnet build Stubby.slnx`, test with `dotnet test --solution Stubby.slnx`. The README has the screenshots (`docs/screenshots/`) and how to run the app.

`tests/Integration.Tests` runs against a real PostgreSQL. It uses `postgres`/`postgres` on localhost, or the connection string in `STUBBY_TEST_PG` (it must be able to create databases). It creates and drops its own database, and skips when no server is reachable, so look for skipped tests in the summary.

## Source of truth

Read these before changing behavior. They are consistent with each other, so keep them that way: when one changes, update the others it affects.

- `docs/business-requirements.md`: scope, statuses, success criteria, test set
- `docs/user-stories.md`: user stories and flow
- `docs/non-functional-requirements.md`: NFR IDs (NFR-P1, NFR-A4, ...) that other docs reference
- `docs/architecture.md`: components, decisions, flow, rules
- `docs/database-design.md`: tables, constraints, queue behavior

## Conventions

- **Architecture:** Clean Architecture, one feature slice per command or query. Dependencies point inward: Web and Infrastructure → Application → Domain. The AI gateway knows nothing about receipts.
- **Database:** follow EF Core defaults. PascalCase plural tables (`Receipts`, `FieldRecords`), PascalCase columns, `PK_`, `IX_` and `CK_` names. Enums are stored as text. Money is `decimal` as `numeric(15,2)`.
- **Docs:** each file starts with a title, `Date:` and `Author:`. Feature specs under `docs/spec/` carry a `Status:` (Built, or Closed for an open-decisions log). Use Mermaid for diagrams. Cite requirement IDs (NFR-xx) instead of restating them.
- **UI:** plain CSS, no framework. Tokens and shared pieces (`.button`, `.panel`, `.chip`, `.banner`) are in `src/Web/wwwroot/app.css`, and each page has its own `*.razor.css`. A class in a page file also matches every element of that page with the same name, so give each thing its own name (a month label and a chart column that were both `.current` broke the chart). Use `::deep` to reach a child component's element.
- **Fake data only:** no real receipts or personal financial data anywhere in the repo (NFR-D4).
- **Secrets:** never commit API keys. Use user-secrets or environment variables (NFR-S3).

## Rules that are easy to break

- Approved receipts are immutable (NFR-R4). They can only be deleted.
- Deleting a receipt keeps its `FieldRecords`, with no link back to the receipt (NFR-D2).
- The gateway does not retry. The worker owns the attempt budget (default 1).
- Subtotal and tax are read only for the flag check. They are never stored.
- Manual entries are tagged and excluded from accuracy numbers.

## Git

`feat/*` branches are cut from `dev` and merged into `dev` by PR. `dev` is merged into `main` by PR.
