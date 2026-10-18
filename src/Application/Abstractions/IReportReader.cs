using Stubby.Domain.Receipts;

namespace Stubby.Application.Abstractions;

/// <summary>Reads Approved receipts for the reports. Only Approved receipts count, by their final values.</summary>
public interface IReportReader
{
    /// <summary>
    /// The sum and count of Approved receipts by category for one month of <c>ReceiptDate</c>. A category with no
    /// receipts is missing from the result. Also returns the first and last month that has an Approved receipt.
    /// </summary>
    Task<MonthlyTotalsData> GetMonthlyTotalsAsync(int year, int month, CancellationToken cancellationToken);

    /// <summary>
    /// Every Approved receipt, or only those of one month when <paramref name="year"/> and <paramref name="month"/> are
    /// both given, ordered by <c>ReceiptDate</c> and then <c>CreatedAt</c>.
    /// </summary>
    Task<IReadOnlyList<ApprovedReceiptRow>> ListApprovedAsync(int? year, int? month, CancellationToken cancellationToken);

    /// <summary>
    /// The sum of Approved receipts for each month from <paramref name="from"/> to <paramref name="to"/>, both included.
    /// A month with no receipts is missing from the result.
    /// </summary>
    Task<IReadOnlyList<MonthSum>> GetMonthlySumsAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken);
}

public readonly record struct YearMonth(int Year, int Month)
{
    public YearMonth AddMonths(int months)
    {
        var index = (Year * 12) + (Month - 1) + months;
        return new YearMonth(index / 12, (index % 12) + 1);
    }

    public int CompareTo(YearMonth other) => ((Year * 12) + Month).CompareTo((other.Year * 12) + other.Month);
}

/// <summary>The sum of the Approved receipts dated in one month.</summary>
public sealed record MonthSum(YearMonth Month, decimal Total);

public sealed record CategorySum(ReceiptCategory Category, decimal Total, int Count);

public sealed record MonthlyTotalsData(IReadOnlyList<CategorySum> Categories, YearMonth? EarliestMonth, YearMonth? LatestMonth);

/// <summary>The final values of an Approved receipt. It holds no receipt id, file name or path.</summary>
public sealed record ApprovedReceiptRow(long TransactionNo, DateOnly Date, string Merchant, ReceiptCategory Category, decimal Total);
