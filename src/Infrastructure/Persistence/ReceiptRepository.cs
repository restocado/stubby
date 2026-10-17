using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence;

// A context per call, not per scope: a Blazor circuit is long-lived and a shared context would not be thread-safe.
internal sealed class ReceiptRepository(IDbContextFactory<StubbyDbContext> dbFactory) : IReceiptRepository
{
    private const string ChecksumIndex = "IX_Receipts_Checksum";
    private const int MaxClaimAttempts = 5;
    private static readonly TimeSpan ClaimRetryDelay = TimeSpan.FromMilliseconds(25);

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

    public async Task<Receipt?> ClaimNextQueuedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The claim can come back empty while Queued receipts remain. If another caller commits the row this
        // one picked before the lock is taken, the row fails the re-check and LIMIT 1 is not refilled. So an
        // empty result is checked against the queue, and tried again a few times while something is still Queued.
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var claimed = await TryClaimOnceAsync(now, cancellationToken);
            if (claimed is not null)
            {
                return claimed;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Receipts.AnyAsync(r => r.Status == ReceiptStatus.Queued, cancellationToken))
            {
                return null;
            }

            await Task.Delay(ClaimRetryDelay, cancellationToken);
        }

        return null;
    }

    private async Task<Receipt?> TryClaimOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // EF has no SKIP LOCKED in LINQ. The row stays locked until the commit, and a caller that finds it
        // locked skips to the next Queued row, so two callers never get the same receipt.
        var claimed = (await db.Receipts
            .FromSql($"""
                SELECT * FROM "Receipts"
                WHERE "Status" = 'Queued'
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (claimed is null)
        {
            return null;
        }

        claimed.StartProcessing(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    public async Task<int> RequeueInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var processing = await db.Receipts
            .Where(r => r.Status == ReceiptStatus.Processing)
            .ToListAsync(cancellationToken);

        // The domain method makes the change, so the rule lives in one place. SaveChanges is one transaction.
        foreach (var receipt in processing)
        {
            receipt.ReturnToQueue(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return processing.Count;
    }

    public async Task SaveAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // The receipt was changed outside this context, so it is attached and every column is written,
        // except the identity column, which the database owns.
        var entry = db.Receipts.Attach(receipt);
        entry.State = EntityState.Modified;
        entry.Property(r => r.TransactionNo).IsModified = false;
        await db.SaveChangesAsync(cancellationToken);
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
