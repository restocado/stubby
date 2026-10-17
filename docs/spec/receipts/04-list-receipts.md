# List Receipts

**Status:** Draft  
**Location:** `Application/Features/Receipts/ListReceipts/`: `ListReceiptsQuery`, `ListReceiptsQueryHandler`, `ListReceiptsQueryValidator`, `ListReceiptsResponse`, `ReceiptListItem`. The query is a Mediator `IQuery<ListReceiptsResponse>`. Uses `IReceiptRepository.ListAsync`, which returns one page of receipts and the total count.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

One page of all receipts, newest first, with status (NFR-P3).

## Behavior

- Input: `Page` (1-based, default 1). Page size 25.
- Order: `CreatedAt` descending, then `Id` descending.
- Output: items, `TotalCount`, `PageCount`.
- Item: `Id`, `TransactionNo`, `OriginalFileName`, `Status`, `IsFlagged`, `CreatedAt`, merchant, date and total (final for Approved, extracted for ForReview, otherwise null), `Category` for Approved, `CanOpen` (ForReview, Failed), `CanDelete` (all but Queued and Processing). Approved receipts can be viewed read-only.
- A page past the end returns an empty list, and `TotalCount` and `PageCount` are still correct.
- With no receipts, `PageCount` is 0.
- An invalid `Page` makes the handler throw FluentValidation's `ValidationException`. The page clamps the query-string value to 1 or more, so the UI never sends one.
- `CanOpen` and `CanDelete` come from the domain (`Receipt.CanBeOpened`, `Receipt.CanBeDeleted`), not from a second copy of the rules.

## Acceptance criteria

- With 60 receipts: pages hold 25, 25, 10 and `PageCount` is 3.
- Order is newest first and stable for equal `CreatedAt`.
- Every item has its `TransactionNo`, and the list shows it as `TXN-000042`.
- All five statuses appear. A flagged receipt has `IsFlagged = true`.
- `CanOpen` and `CanDelete` follow the rules above.
- A Queued receipt shows null merchant, date and total. An Approved one shows final values and category.
- `Page` of 0 or less fails validation.
- A page loads in under 2 seconds with 1 000 receipts (NFR-P4).
