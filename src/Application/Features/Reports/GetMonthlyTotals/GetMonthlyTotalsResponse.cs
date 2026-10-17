using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Reports.GetMonthlyTotals;

/// <param name="Categories">All eight categories, in enum order, with 0.00 and 0 for one that has no receipts.</param>
/// <param name="EarliestMonth">The first month that has an Approved receipt, or null when there are none.</param>
/// <param name="LatestMonth">The last month that has an Approved receipt, or null when there are none.</param>
public sealed record GetMonthlyTotalsResponse(
    int Year,
    int Month,
    IReadOnlyList<CategoryTotal> Categories,
    decimal GrandTotal,
    int GrandCount,
    YearMonth? EarliestMonth,
    YearMonth? LatestMonth);

public sealed record CategoryTotal(ReceiptCategory Category, decimal Total, int Count);
