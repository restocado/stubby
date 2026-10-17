# Stubby

Single-user .NET 10 Blazor app that reads receipts with a vision model. See [README.md](README.md) for the overview.

## Status

Scaffolded. `docs/` holds the design and `src/` holds empty projects wired per `docs/architecture.md`; no feature code yet. Only architecture tests exist in `tests/`. Build with `dotnet build Stubby.slnx`, test with `dotnet test --solution Stubby.slnx`.

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
- **Docs:** each file starts with a title, `Date:` and `Author:`. Use Mermaid for diagrams. Cite requirement IDs (NFR-xx) instead of restating them.
- **Fake data only:** no real receipts or personal financial data anywhere in the repo (NFR-D4).
- **Secrets:** never commit API keys. Use user-secrets or environment variables (NFR-S3).

## Rules that are easy to break

- Approved receipts are immutable (NFR-R4). They can only be deleted.
- Deleting a receipt keeps its `FieldRecords`, with no link back to the receipt (NFR-D2).
- The gateway does not retry. The worker owns the attempt budget (default 1).
- Subtotal and tax are read only for the flag check. They are never stored.
- Manual entries are tagged and excluded from accuracy numbers.

## Git

The working branch is `docs`; PRs go to `main`.
