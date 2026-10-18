# Flagging Rules

**Status:** Built  
**Location:** `Domain/Receipts/Flagging/`: `ReceiptFlagEvaluator`, `FlagReason`, `FlagResult`. Inputs include `TotalLimit` and today's date, passed in.  
**Depends on:** nothing.

## Scope

Decide from an extraction result whether a receipt is flagged, and why (NFR-A4). Pure, no I/O.

## Behavior

A receipt is flagged if any rule is true. Each true rule adds its reason.

| Reason | Rule |
|---|---|
| `TotalMissing` | Total is missing. |
| `TotalNotPositive` | Total is zero or negative. |
| `TotalAtOrAboveLimit` | Total is at or above the limit (3000 to start with). |
| `SubtotalTaxMismatch` | Subtotal and tax are both present and subtotal + tax does not equal the total. Exact decimal comparison. |
| `DateMissing` | Date is missing. |
| `DateInFuture` | Date is after today. |
| `DateTooOld` | Date is more than one year before today. |

A missing total gives only `TotalMissing`. Merchant is not checked. Without a tax, the subtotal check is skipped. Reasons are stored comma-separated in `FlagReasons`.

A receipt with a tip, a surcharge or a service charge has a total above subtotal + tax, so it is flagged `SubtotalTaxMismatch`. That is accepted for now: the user sees the flag in review and approves. Skipping the check when the total is higher, or reading a tip field, were considered and left out.

## Acceptance criteria

- Total 25.00 with a recent date is not flagged.
- Missing, `0` and `-5.00` totals are flagged with the right reason.
- With limit 3000: 2999.99 is not flagged; 3000.00 and 3000.01 are.
- Subtotal 10.00, tax 0.80, total 10.80 is not flagged. Total 10.00 is flagged.
- Subtotal without tax, and tax without subtotal, skip the check.
- Today is not flagged, tomorrow is. With today 2026-10-17, 2025-10-17 is not flagged and 2025-10-16 is.
- A receipt breaking two rules shows both reasons.
- Changing the limit to 5000 unflags a 4000.00 total.
- The evaluator reads no clock, settings or database.
