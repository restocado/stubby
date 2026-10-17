# Export Receipts CSV

**Status:** Draft  
**Location:** `Application/Features/Reports/ExportReceiptsCsv/`: `ExportReceiptsCsvQuery`, `ExportReceiptsCsvQueryHandler`, `ExportReceiptsCsvResponse`. Endpoint in Web: `GET /api/reports/receipts.csv`, linked from the monthly totals page.  
**Depends on:** receipts 01 and 02.

## Scope

Export Approved receipts as a CSV that opens correctly in Excel (NFR-D3).

## Behavior

- Columns: `TransactionNo`, `Date`, `Merchant`, `Category`, `Total`. Final values. Ordered by `ReceiptDate`, then `CreatedAt`.
- UTF-8 with BOM, comma separator, CRLF, header row.
- Fields with a comma, quote or line break are quoted, and inner quotes doubled.
- Date as `yyyy-MM-dd`. Total as a plain number with two decimals, invariant culture, no symbol.
- A merchant starting with `=`, `+`, `-` or `@` is prefixed so Excel does not run it as a formula.
- File name `stubby-receipts-<yyyyMMdd>.csv`. No receipt id, file name or path is exported.

## Acceptance criteria

- With 3 Approved and 4 other receipts: header plus exactly 3 rows.
- A deleted Approved receipt is not in the file.
- `Smith, Jones & Co` stays in one column. A quote is doubled. `=SUM(A1)` opens as text.
- `Café Müller` shows correctly when opened in Excel.
- Each row's `TransactionNo` is the receipt's number in the form `TXN-000042`.
- 1234.50 exports as `1234.50`. Dates are `yyyy-MM-dd` whatever the server culture.
- With no Approved receipts the file has only the header and the download succeeds.
- The file starts with the bytes `EF BB BF`.
