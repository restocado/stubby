# Receipts Module : Test Plan

**Status:** Built

Scenarios across several specs. Integration tests use a separate PostgreSQL test database and a fake extractor.

## Lifecycle

1. Upload, process (success), open, approve: Queued → Processing → ForReview → Approved, 3 field records, in monthly totals.
2. Extractor fails with max 1: Failed after one attempt.
3. Extractor fails with max 3: two requeues, then Failed.
4. Failed receipt reprocessed and extractor now succeeds: ForReview, no new upload.
5. Failed receipt entered manually and approved: manual records, excluded from the accuracy query.

## Duplicates

6. Same file twice: second rejected with the first receipt's transaction number, one file on disk.
7. Same file as an Approved receipt: rejected with its number.
8. Delete, then upload again: accepted.
9. Two identical simultaneous uploads: one accepted.

## Locking and immutability

10. Queued and Processing receipts cannot be opened, approved, deleted or have their image fetched, by UI or direct call.
11. An Approved receipt cannot be approved again, reprocessed or edited by any path. Only delete works.
12. Approve and delete at the same time give a clean result, never a half-approved row.

## Transaction number

13. Upload 20 different files at once: 20 distinct numbers.
14. A receipt keeps its number through processing, failure, reprocess and approval.
15. Delete the highest-numbered receipt, upload a new one: the new number is higher than the deleted one.

## Auditability

16. Approve 10 receipts, delete 5: the accuracy query gives the same counts before and after.
17. After delete, `FieldRecords` holds no receipt id, transaction number, file name or path.

## Reliability and performance

18. Restart with Queued and Processing receipts: none lost.
19. Provider down: receipts end Failed, no unhandled errors, "reprocess all failed" recovers them.
20. Upload while the worker is busy returns at once.
21. With 1 000 receipts, list and review each load in under 2 seconds.
