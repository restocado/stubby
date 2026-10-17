# Extraction Module : Test Plan

**Status:** Draft

Scenarios across several specs. Integration tests use a separate test database, a fake extractor and a fake provider.

## End to end

1. Clean result: ForReview, not flagged.
2. Total 3000.00 with limit 3000: ForReview, flagged.
3. Subtotal 10.00, tax 0.80, total 12.00: flagged, and no subtotal or tax is stored.
4. Extractor throws with max 1: Failed after one call.
5. Extractor throws twice then succeeds with max 3: ForReview, `AttemptsUsed = 2`.

## Gateway

6. Provider returns 500 once: called once per attempt, no gateway retry.
7. Max 3 and a provider that always fails: called 3 times, not 9.
8. Provider unreachable for a batch: all Failed, app stays responsive.
9. Circuit opens: later receipts fail fast. After the break and "reprocess all failed" they succeed.

## PDF

10. A 3-page PDF reaches the provider as 3 images in order, in one request.
11. A corrupt PDF becomes Failed through the normal attempt path.
12. Two PDFs rendered at once each return correct pages.

## Restart

13. Kill the app mid-processing: the receipt is processed after restart, `AttemptsUsed` unchanged.
14. Stop cleanly mid-extraction: processed once after restart.

## Privacy and accuracy

15. No log line contains merchant names, amounts or image bytes from the test receipts.
16. Requests go only to the configured provider over HTTPS.
17. Run the eval tool against the real model: total ≥ 90%, merchant and date ≥ 85%, flag coverage ≥ 95%. Rerun after every model or prompt change.
