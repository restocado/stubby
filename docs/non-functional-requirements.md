# Stubby : Non-Functional Requirements

Date: 2026-10-17  
Author: restocado

Status: accepted. Open questions at the end are still to be decided. Accuracy and performance targets come from [business-requirements.md](business-requirements.md).

## Performance

| ID | Requirement |
|---|---|
| NFR-P1 | Median time from upload to a reviewed, saved receipt is under 60 seconds for a single-page receipt. |
| NFR-P2 | Uploading does not block on extraction. The user can upload another receipt immediately. |
| NFR-P3 | The receipts list is paged, showing 25 receipts per page, newest first. |
| NFR-P4 | A page of the receipts list and the review screen load in under 2 seconds. |

## Accuracy

| ID | Requirement |
|---|---|
| NFR-A1 | The total is extracted correctly on at least 90% of the 30 synthetic receipts. |
| NFR-A2 | Merchant and date are correct on at least 85% of the same set. |
| NFR-A3 | Of the receipts where the total is wrong, at least 95% are flagged for review instead of being saved silently. |

## Reliability

| ID | Requirement |
|---|---|
| NFR-R1 | A failed extraction is retried automatically, up to 3 attempts in total per receipt. After that the receipt becomes Failed. |
| NFR-R2 | A receipt is never lost. Queued and Processing receipts survive an app restart and continue processing. |
| NFR-R3 | If the vision model provider is unavailable, receipts use up their attempts and become Failed. The user can reprocess them, one by one or all at once, once the provider is back. The app keeps working and shows no unhandled errors. |
| NFR-R4 | An approved receipt cannot be edited by any path in the app. It can only be deleted. |

## Usability and compatibility

| ID | Requirement |
|---|---|
| NFR-U1 | The web UI works in the current versions of Chrome, Edge and Firefox. |
| NFR-U2 | The web UI is usable in a phone browser, including uploading a photo from the phone. There is no native mobile app. |
| NFR-U3 | Flagged receipts are visibly marked in the receipts list and on the review screen. |
| NFR-U4 | An unsupported file type is rejected with a clear error message before anything is queued. |

## Data and auditability

| ID | Requirement |
|---|---|
| NFR-D1 | Every correction made on the review screen is stored with the before and after values, so a before/after accuracy report can be generated. |
| NFR-D2 | When a receipt is deleted, its corrections are kept as anonymous records: field name, extracted value, corrected value, timestamp, and model and prompt version. The image and any link to the receipt are deleted. |
| NFR-D3 | The CSV export contains approved receipts only, and opens correctly in Excel. |
| NFR-D4 | Only fake or synthetic receipts are used in the demo. No real personal or financial data is stored. |

## Security and privacy

| ID | Requirement |
|---|---|
| NFR-S1 | Uploaded files are checked for type and size (maximum 10 MB) before they are queued. There is no limit on the number of pages in a PDF for now. |
| NFR-S2 | Receipt images are sent only to the configured vision model provider, over HTTPS. |
| NFR-S3 | API keys and other secrets are not stored in source control. |

## Maintainability and portability

| ID | Requirement |
|---|---|
| NFR-M1 | The app is built on .NET 10 and Blazor. |
| NFR-M2 | Extraction sits behind an interface, so the vision model provider can be changed without touching the rest of the app. |
| NFR-M3 | The 30-receipt test set can be rerun on demand to measure accuracy after a model or prompt change. |
