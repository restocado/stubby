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
}

public readonly record struct YearMonth(int Year, int Month);

public sealed record CategorySum(ReceiptCategory Category, decimal Total, int Count);

public sealed record MonthlyTotalsData(IReadOnlyList<CategorySum> Categories, YearMonth? EarliestMonth, YearMonth? LatestMonth);

/// <summary>The final values of an Approved receipt. It holds no receipt id, file name or path.</summary>
public sealed record ApprovedReceiptRow(long TransactionNo, DateOnly Date, string Merchant, ReceiptCategory Category, decimal Total);
