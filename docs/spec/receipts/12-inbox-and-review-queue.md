# Inbox and Review Queue

**Status:** Built  
**Location:** `Application/Features/Receipts/GetInbox/`, `GetReviewQueue/` and `CountReceiptsToReview/`: a Mediator query, handler and response each. Use `IReceiptRepository.GetInboxAsync`, `ListForReviewIdsAsync` and `CountForReviewAsync`.  
**Depends on:** 01-domain-model, 02-persistence.

## Scope

What needs attention (the inbox), where a receipt stands among the receipts waiting for review and which one follows it (the review queue), and the number for the menu.

## Behavior

- **Inbox** (`GetInboxQuery`): the ForReview receipts oldest first (by `CreatedAt`, then `Id`), the Failed receipts newest first, and how many are Queued or Processing. Each list is cut to 50 items, and the counts are the whole numbers. An item has `Id`, `TransactionNo`, `OriginalFileName`, `IsPdf`, what was read (merchant, date, total), the flag reasons (a list of `FlagReason`, with any name this version does not know skipped) and, for a Failed receipt, its last error.
- **Review queue** (`GetReviewQueueQuery(CurrentId)`): the ForReview receipts oldest first. It returns the 1-based `Position` of the current receipt, the `Count` waiting, and `NextId`.
  - `NextId` is the receipt after the current one. After the newest comes the oldest again, so a skipped receipt comes round again.
  - When the current receipt is the only one waiting, `NextId` is null.
  - When the current receipt is not waiting (Failed, Approved or gone), `Position` is null and `NextId` is the oldest waiting, or null when none is.
  - The queue is read again on each request and nothing is kept between requests, so a receipt that arrives or is approved in another tab is taken into account.
- **Count** (`CountReceiptsToReviewQuery`): how many receipts are ForReview. It is the number beside Inbox in the menu.
- Queued and Processing receipts are never in the inbox lists or the queue. They are only counted as being read.

## Acceptance criteria

- The inbox lists ForReview oldest first and Failed newest first, with the counts, and cuts a long list to the limit while keeping the whole count.
- Queued and Processing receipts are counted as being read and not listed. Approved receipts are not in the inbox.
- The queue gives the place and the next receipt, goes round after the newest, has no next for a single receipt, and gives the oldest waiting for a receipt that is not waiting.
- The count is the number of ForReview receipts.
