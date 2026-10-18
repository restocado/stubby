using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Reports.GetTotalsTrend;

/// <param name="Months">12 months, oldest first, the last being the month asked for. A month with no receipts is 0.00.</param>
/// <param name="PreviousMonthTotal">The month before the one asked for, 0.00 when it has no receipts.</param>
/// <param name="ChangePercent">
/// How much the month asked for is above (positive) or below (negative) the month before, in percent, rounded to
/// one decimal. Null when the month before has nothing to compare with.
/// </param>
/// <param name="AveragePerMonth">The average of the months that have receipts, 0.00 when none do.</param>
/// <param name="MonthsWithReceipts">How many of the 12 months have receipts.</param>
public sealed record GetTotalsTrendResponse(
    IReadOnlyList<MonthTotal> Months,
    decimal PreviousMonthTotal,
    decimal? ChangePercent,
    decimal AveragePerMonth,
    int MonthsWithReceipts);

public sealed record MonthTotal(YearMonth Month, decimal Total);
