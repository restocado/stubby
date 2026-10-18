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

    public async Task<ReceiptPage> ListAsync(ReceiptFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var matching = Filter(db.Receipts.AsNoTracking(), filter);
        var totalCount = await matching.CountAsync(cancellationToken);

        // Long math, so a huge page number cannot overflow.
        if ((page - 1L) * pageSize >= totalCount)
        {
            return new ReceiptPage([], totalCount);
        }

        var items = await matching
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new ReceiptPage(items, totalCount);
    }

    private static IQueryable<Receipt> Filter(IQueryable<Receipt> receipts, ReceiptFilter filter)
    {
        receipts = filter.Status switch
        {
            ReceiptStatusFilter.ToReview => receipts.Where(r => r.Status == ReceiptStatus.ForReview),
            ReceiptStatusFilter.Approved => receipts.Where(r => r.Status == ReceiptStatus.Approved),
            ReceiptStatusFilter.Failed => receipts.Where(r => r.Status == ReceiptStatus.Failed),
            _ => receipts,
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // The final merchant when there is one (Approved), otherwise what was read. A file name matches too, because a
            // receipt that could not be read has no merchant. The pattern's own wildcards are escaped.
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            receipts = receipts.Where(r =>
                EF.Functions.ILike(r.Merchant ?? r.ExtractedMerchant ?? string.Empty, pattern, @"\")
                || EF.Functions.ILike(r.OriginalFileName, pattern, @"\"));
        }

        if (filter.Year is { } year && filter.Month is { } month)
        {
            var first = new DateOnly(year, month, 1);
            var next = first.AddMonths(1);
            receipts = receipts.Where(r => (r.ReceiptDate ?? r.ExtractedDate) >= first && (r.ReceiptDate ?? r.ExtractedDate) < next);
        }

        if (filter.Category is { } category)
        {
            receipts = receipts.Where(r => r.Category == category);
        }

        return receipts;
    }

    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public async Task<InboxData> GetInboxAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var receipts = db.Receipts.AsNoTracking();

        var forReviewCount = await receipts.CountAsync(r => r.Status == ReceiptStatus.ForReview, cancellationToken);
        var forReview = await receipts
            .Where(r => r.Status == ReceiptStatus.ForReview)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var failedCount = await receipts.CountAsync(r => r.Status == ReceiptStatus.Failed, cancellationToken);
        var failed = await receipts
            .Where(r => r.Status == ReceiptStatus.Failed)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var reading = await receipts.CountAsync(
            r => r.Status == ReceiptStatus.Queued || r.Status == ReceiptStatus.Processing, cancellationToken);

        return new InboxData(forReview, forReviewCount, failed, failedCount, reading);
    }

    public async Task<IReadOnlyList<Guid>> ListForReviewIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Receipts
            .AsNoTracking()
            .Where(r => r.Status == ReceiptStatus.ForReview)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountForReviewAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.ForReview, cancellationToken);
    }

    public async Task<Receipt?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Receipts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<ReceiptWriteOutcome> ApproveAsync(
        Guid id,
        Func<Receipt, IReadOnlyList<FieldRecord>?> approve,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await LockAsync(db, id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptWriteOutcome.NotFound;
        }

        var records = approve(receipt);
        if (records is null)
        {
            return ReceiptWriteOutcome.Conflict;
        }

        db.FieldRecords.AddRange(records);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReceiptWriteOutcome.Done;
    }

    public async Task<ReceiptWriteOutcome> ReprocessAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await LockAsync(db, id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptWriteOutcome.NotFound;
        }

        if (receipt.Status != ReceiptStatus.Failed)
        {
            return ReceiptWriteOutcome.Conflict;
        }

        receipt.Reprocess(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReceiptWriteOutcome.Done;
    }

    public async Task<int> ReprocessAllFailedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Locked, so a receipt cannot be deleted or approved while it is being requeued.
        var failed = await db.Receipts
            .FromSql($"""SELECT * FROM "Receipts" WHERE "Status" = 'Failed' FOR UPDATE""")
            .ToListAsync(cancellationToken);

        // The domain method makes the change, so the rule lives in one place.
        foreach (var receipt in failed)
        {
            receipt.Reprocess(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return failed.Count;
    }

    public async Task<ReceiptDeletion> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await LockAsync(db, id, cancellationToken);
        if (receipt is null)
        {
            return new ReceiptDeletion(ReceiptWriteOutcome.NotFound);
        }

        // Queued and Processing receipts are being read, so they cannot be deleted.
        if (!receipt.CanBeDeleted)
        {
            return new ReceiptDeletion(ReceiptWriteOutcome.Conflict);
        }

        // Only the row goes. Field records hold no link to the receipt, so they stay as they are.
        db.Receipts.Remove(receipt);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReceiptDeletion(ReceiptWriteOutcome.Done, receipt.TransactionNo, receipt.ImagePath);
    }

    // The row lock makes a second change to the same receipt, such as another approval or a delete, wait for this
    // transaction and then see its result, so a receipt cannot be approved twice or changed after it is deleted.
    private static async Task<Receipt?> LockAsync(StubbyDbContext db, Guid id, CancellationToken cancellationToken) =>
        (await db.Receipts
            .FromSql($"""SELECT * FROM "Receipts" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(cancellationToken)).SingleOrDefault();

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
