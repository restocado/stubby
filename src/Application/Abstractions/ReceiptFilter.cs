using Stubby.Domain.Receipts;

namespace Stubby.Application.Abstractions;

/// <summary>Which receipts the list shows. A receipt that is still being read only appears under <see cref="All"/>.</summary>
public enum ReceiptStatusFilter
{
    All,
    ToReview,
    Approved,
    Failed,
}

/// <summary>
/// What the receipts list is narrowed to. Every part is optional. <paramref name="Search"/> matches the merchant
/// or the file name, ignoring case. A month is the month of the receipt's date, so it needs both numbers.
/// A category only matches Approved receipts, because that is the only time a receipt has one.
/// </summary>
public sealed record ReceiptFilter(
    string? Search = null,
    ReceiptStatusFilter Status = ReceiptStatusFilter.All,
    int? Year = null,
    int? Month = null,
    ReceiptCategory? Category = null);
