# Stubby : Business Requirements Document

Date: 2026-10-17  
Author: restocado

## Problem

People who track spending keep a pile of receipts and type each one into a spreadsheet by hand. It is tedious, so they fall behind, and by month-end they are guessing at totals and categories.

## Goal

Take a photo of a receipt, check the details Stubby picked up, and see where this month's money went.

## How it works

1. Upload a receipt image (JPG, PNG, or PDF).
2. A background job sends the image to the vision model, which fills in the merchant, date, and total.
3. A review screen shows the receipt beside the extracted fields. The user confirms or corrects them.
4. Approved receipts feed monthly totals by category, with CSV export.

## Users

People who want a monthly spending view without typing receipts into a spreadsheet.

## Out of scope

- Bank or card account syncing
- Mobile app (the web UI only needs to work in a phone browser)
- Multi-currency and tax-filing exports
- Shared or multi-user budgets
- Handwritten receipts

## Success criteria

1. The total is extracted correctly on at least 90% of a set of 30 synthetic receipts.
2. Merchant and date are correct on at least 85% of the same set.
3. Of the receipts where extraction got the total wrong, at least 95% are flagged for review instead of being saved silently.
4. Median time from upload to a reviewed, saved receipt is under 60 seconds for a single-page receipt.
5. Every correction made in the review screen is stored, so a before/after accuracy report can be generated.

## Constraints and assumptions

- .NET 10 and Blazor
- Fake or synthetic receipts only for the demo
- Extraction uses a vision model behind an interface, so the provider can change

## Open questions

- [ ] Which model and provider to start with
- [ ] How the 30 sample receipts will be created (varied layouts, plus a few blurry photos)
- [ ] Background job approach