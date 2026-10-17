# Export Receipts CSV

**Status:** Draft  
**Location:** `Application/Features/Reports/ExportReceiptsCsv/`: `ExportReceiptsCsvQuery`, `ExportReceiptsCsvQueryValidator`, `ExportReceiptsCsvQueryHandler`, `ExportReceiptsCsvResponse`, and the internal `ReceiptsCsv` writer. A Mediator query that uses `IReportReader.ListApprovedAsync`. Endpoint in Web (`Web/Endpoints/ReportEndpoints.cs`): `GET /api/reports/receipts.csv`, linked from the monthly totals page.  
**Depends on:** receipts 01 and 02.

## Scope

Export Approved receipts as a CSV that opens correctly in Excel (NFR-D3).

## Behavior

- Without parameters the file holds every Approved receipt. With both `year` and `month` it holds that month's receipts, by `ReceiptDate`. Giving only one of them, or an impossible month, is a 400 with a short message (the handler throws `ValidationException`, which the endpoint turns into the 400).
- Columns: `TransactionNo`, `Date`, `Merchant`, `Category`, `Total`. Final values. Ordered by `ReceiptDate`, then `CreatedAt`.
- UTF-8 with BOM, comma separator, CRLF after every line including the last, header row. The file is written by hand and does not depend on the server's culture.
- Fields with a comma, quote or line break are quoted, and inner quotes doubled.
- Date as `yyyy-MM-dd`. Total as a plain number with two decimals, invariant culture, no symbol. The category is in lowercase, as the fixed list is written in the BRD. `TransactionNo` is in the form `TXN-000042`.
- A merchant starting with `=`, `+`, `-`, `@`, a tab or a carriage return is prefixed with a single quote, so Excel shows it as text and does not run it as a formula.
- File name `stubby-receipts-<yyyyMMdd>.csv`, dated by the server's local date, and `stubby-receipts-<yyyy>-<MM>.csv` for a month. No receipt id, file name or path is exported: the rows the query returns do not hold them.
- The response has `Content-Type: text/csv; charset=utf-8`, `X-Content-Type-Options: nosniff` and `Cache-Control: private, no-store`.

## Acceptance criteria

- With 3 Approved and 4 other receipts: header plus exactly 3 rows.
- A deleted Approved receipt is not in the file.
- `Smith, Jones & Co` stays in one column. A quote is doubled. `=SUM(A1)` opens as text.
- `Café Müller` shows correctly when opened in Excel. Tests check that the bytes are UTF-8 after the byte order mark. Opening the file in Excel itself is a manual check that has not been done.
- Each row's `TransactionNo` is the receipt's number in the form `TXN-000042`.
- 1234.50 exports as `1234.50`. Dates are `yyyy-MM-dd` whatever the server culture.
- With no Approved receipts the file has only the header and the download succeeds.
- The file starts with the bytes `EF BB BF`.
