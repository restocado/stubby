# List Receipts

**Status:** Draft  
**Location:** `Application/Features/Receipts/ListReceipts/`: `ListReceiptsQuery`, `ListReceiptsQueryHandler`, `ListReceiptsQueryValidator`, `ListReceiptsResponse`, `ReceiptListItem`. The query is a Mediator `IQuery<ListReceiptsResponse>`. Uses `IReceiptRepository.ListAsync`, which takes a `ReceiptFilter` and returns one page of the matching receipts with the number that match.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

One page of the receipts, newest first, with status (NFR-P3), narrowed by search, status, month and category.

## Behavior

- Input: `Page` (1-based, default 1), and the filters below, each optional. Page size 25.
- **Search:** part of the merchant (the final one for an Approved receipt, otherwise what was read) or of the file name, ignoring case. The file name is there because a receipt that could not be read has no merchant. Leading and trailing spaces are dropped, and a blank search is no search. The characters `%` and `_`, and the backslash, are ordinary characters in a search. At most 100 characters.
- **Status:** `All` (including receipts still being read), `ToReview`, `Approved` or `Failed`.
- **Month:** `Year` and `Month` together, the month of the receipt's date (final for Approved, otherwise what was read). A receipt with no date is in no month.
- **Category:** an Approved receipt's category. Other receipts have none, so they never match.
- The filters combine, and `TotalCount` and `PageCount` count the matches only.
- Order: `CreatedAt` descending, then `Id` descending.
- Output: items, `TotalCount`, `PageCount`.
- Item: `Id`, `TransactionNo`, `OriginalFileName`, `Status`, `IsFlagged`, `CreatedAt`, merchant, date and total (final for Approved, extracted for ForReview, otherwise null), `Category` for Approved, `CanOpen` (ForReview, Failed), `CanDelete` (all but Queued and Processing). Approved receipts can be viewed read-only.
- A page past the end returns an empty list, and `TotalCount` and `PageCount` are still correct.
- With no receipts, `PageCount` is 0.
- An invalid `Page` makes the handler throw FluentValidation's `ValidationException`. The page clamps the query-string value to 1 or more, so the UI never sends one.
- `CanOpen` and `CanDelete` come from the domain (`Receipt.CanBeOpened`, `Receipt.CanBeDeleted`), not from a second copy of the rules.

## Acceptance criteria

- With 60 receipts: pages hold 25, 25, 10 and `PageCount` is 3.
- Search finds part of a merchant in any case, uses the final merchant of an Approved receipt, finds a failed receipt by its file name, and treats `%` and `_` as ordinary characters.
- The status, month and category filters pick the right receipts, with the first and last day of a month included, and they combine.
- A search over 100 characters, a month without its year, a month outside 1 to 12 and a year outside 1 to 9998 fail validation. The page turns a bad value in the address into no filter.
- Order is newest first and stable for equal `CreatedAt`.
- Every item has its `TransactionNo`, and the list shows it as `TXN-000042`.
- All five statuses appear. A flagged receipt has `IsFlagged = true`.
- `CanOpen` and `CanDelete` follow the rules above.
- A Queued receipt shows null merchant, date and total. An Approved one shows final values and category.
- `Page` of 0 or less fails validation.
- A page loads in under 2 seconds with 1 000 receipts (NFR-P4).
