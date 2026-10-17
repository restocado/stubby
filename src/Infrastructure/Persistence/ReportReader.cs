using Microsoft.EntityFrameworkCore;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence;

internal sealed class ReportReader(IDbContextFactory<StubbyDbContext> dbFactory) : IReportReader
{
    public async Task<MonthlyTotalsData> GetMonthlyTotalsAsync(int year, int month, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var approved = db.Receipts.AsNoTracking().Where(r => r.Status == ReceiptStatus.Approved);

        // A month of the receipt's own date, and not of the day it was uploaded. numeric sums are exact.
        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);
        var sums = await approved
            .Where(r => r.ReceiptDate >= first && r.ReceiptDate < next && r.Category != null)
            .GroupBy(r => r.Category!.Value)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Total) ?? 0m, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var earliest = await approved.MinAsync(r => r.ReceiptDate, cancellationToken);
        var latest = await approved.MaxAsync(r => r.ReceiptDate, cancellationToken);

        return new MonthlyTotalsData(
            sums.Select(s => new CategorySum(s.Category, s.Total, s.Count)).ToList(),
            earliest is { } e ? new YearMonth(e.Year, e.Month) : null,
            latest is { } l ? new YearMonth(l.Year, l.Month) : null);
    }

    public async Task<IReadOnlyList<ApprovedReceiptRow>> ListApprovedAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var approved = db.Receipts.AsNoTracking().Where(r => r.Status == ReceiptStatus.Approved);

        if (year is { } y && month is { } m)
        {
            var first = new DateOnly(y, m, 1);
            var next = first.AddMonths(1);
            approved = approved.Where(r => r.ReceiptDate >= first && r.ReceiptDate < next);
        }

        var rows = await approved
            .OrderBy(r => r.ReceiptDate)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new { r.TransactionNo, r.ReceiptDate, r.Merchant, r.Category, r.Total })
            .ToListAsync(cancellationToken);

        // The Approved check constraint makes every one of these not null.
        return rows
            .Select(r => new ApprovedReceiptRow(r.TransactionNo, r.ReceiptDate!.Value, r.Merchant!, r.Category!.Value, r.Total!.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<MonthSum>> GetMonthlySumsAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var first = new DateOnly(from.Year, from.Month, 1);
        var next = new DateOnly(to.Year, to.Month, 1).AddMonths(1);
        var sums = await db.Receipts
            .AsNoTracking()
            .Where(r => r.Status == ReceiptStatus.Approved && r.ReceiptDate >= first && r.ReceiptDate < next)
            .GroupBy(r => new { r.ReceiptDate!.Value.Year, r.ReceiptDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(r => r.Total) ?? 0m })
            .ToListAsync(cancellationToken);

        return sums.Select(s => new MonthSum(new YearMonth(s.Year, s.Month), s.Total)).ToList();
    }
}
