using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence;

// A context per call, not per scope: a Blazor circuit is long-lived and a shared context would not be thread-safe.
internal sealed class ReceiptRepository(IDbContextFactory<StubbyDbContext> dbFactory) : IReceiptRepository
{
    private const string ChecksumIndex = "IX_Receipts_Checksum";

    public async Task<long?> FindTransactionNoByChecksumAsync(string checksum, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Receipts
            .Where(r => r.Checksum == checksum)
            .Select(r => (long?)r.TransactionNo)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ReceiptPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var totalCount = await db.Receipts.CountAsync(cancellationToken);

        // Long math, so a huge page number cannot overflow.
        if ((page - 1L) * pageSize >= totalCount)
        {
            return new ReceiptPage([], totalCount);
        }

        var items = await db.Receipts
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new ReceiptPage(items, totalCount);
    }

    public async Task<bool> TryAddAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Receipts.Add(receipt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ChecksumIndex,
        })
        {
            return false;
        }
    }
}
