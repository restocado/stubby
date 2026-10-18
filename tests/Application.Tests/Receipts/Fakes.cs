using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

internal sealed class FakeReceiptRepository : IReceiptRepository
{
    private long _lastTransactionNo;

    public List<Receipt> Receipts { get; } = [];

    /// <summary>Makes the next <see cref="TryAddAsync"/> report a checksum clash, as if a race was lost.</summary>
    public Receipt? ReceiptToAddBeforeNextInsert { get; set; }

    public Exception? ExceptionOnAdd { get; set; }

    public Task<long?> FindTransactionNoByChecksumAsync(string checksum, CancellationToken cancellationToken) =>
        Task.FromResult(Receipts.FirstOrDefault(r => r.Checksum == checksum)?.TransactionNo);

    /// <summary>The filter the last <see cref="ListAsync"/> was called with.</summary>
    public ReceiptFilter? LastFilter { get; private set; }

    public Task<ReceiptPage> ListAsync(ReceiptFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var matching = Receipts.Where(r => Matches(r, filter)).ToList();
        var items = matching
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(new ReceiptPage(items, matching.Count));
    }

    private static bool Matches(Receipt receipt, ReceiptFilter filter)
    {
        var statusMatches = filter.Status switch
        {
            ReceiptStatusFilter.ToReview => receipt.Status == ReceiptStatus.ForReview,
            ReceiptStatusFilter.Approved => receipt.Status == ReceiptStatus.Approved,
            ReceiptStatusFilter.Failed => receipt.Status == ReceiptStatus.Failed,
            _ => true,
        };
        var merchant = receipt.Merchant ?? receipt.ExtractedMerchant ?? string.Empty;
        var searchMatches = string.IsNullOrWhiteSpace(filter.Search)
            || merchant.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
            || receipt.OriginalFileName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase);
        var date = receipt.ReceiptDate ?? receipt.ExtractedDate;
        var monthMatches = filter.Year is not { } year || filter.Month is not { } month
            || (date is { } d && d.Year == year && d.Month == month);
        var categoryMatches = filter.Category is null || receipt.Category == filter.Category;
        return statusMatches && searchMatches && monthMatches && categoryMatches;
    }

    public Task<InboxData> GetInboxAsync(int limit, CancellationToken cancellationToken)
    {
        var forReview = Receipts.Where(r => r.Status == ReceiptStatus.ForReview).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
        var failed = Receipts.Where(r => r.Status == ReceiptStatus.Failed).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToList();
        var reading = Receipts.Count(r => r.Status is ReceiptStatus.Queued or ReceiptStatus.Processing);
        return Task.FromResult(new InboxData(forReview.Take(limit).ToList(), forReview.Count, failed.Take(limit).ToList(), failed.Count, reading));
    }

    public Task<IReadOnlyList<Guid>> ListForReviewIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Receipts
            .Where(r => r.Status == ReceiptStatus.ForReview)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => r.Id)
            .ToList());

    public Task<int> CountForReviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Receipts.Count(r => r.Status == ReceiptStatus.ForReview));

    public List<FieldRecord> FieldRecords { get; } = [];

    /// <summary>Runs after every <see cref="FindByIdAsync"/>, so a test can change a receipt behind a handler's back.</summary>
    public Action<Receipt?>? AfterFind { get; set; }

    public Task<Receipt?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = Receipts.FirstOrDefault(r => r.Id == id);
        AfterFind?.Invoke(receipt);
        return Task.FromResult(receipt);
    }

    public Task<ReceiptWriteOutcome> ApproveAsync(
        Guid id, Func<Receipt, IReadOnlyList<FieldRecord>?> approve, CancellationToken cancellationToken)
    {
        var receipt = Receipts.FirstOrDefault(r => r.Id == id);
        if (receipt is null)
        {
            return Task.FromResult(ReceiptWriteOutcome.NotFound);
        }

        var records = approve(receipt);
        if (records is null)
        {
            return Task.FromResult(ReceiptWriteOutcome.Conflict);
        }

        FieldRecords.AddRange(records);
        return Task.FromResult(ReceiptWriteOutcome.Done);
    }

    /// <summary>When set, <see cref="DeleteAsync"/> throws it, as a database failure would.</summary>
    public Exception? ExceptionOnDelete { get; set; }

    public Task<ReceiptWriteOutcome> ReprocessAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var receipt = Receipts.FirstOrDefault(r => r.Id == id);
        if (receipt is null)
        {
            return Task.FromResult(ReceiptWriteOutcome.NotFound);
        }

        if (receipt.Status != ReceiptStatus.Failed)
        {
            return Task.FromResult(ReceiptWriteOutcome.Conflict);
        }

        receipt.Reprocess(now);
        return Task.FromResult(ReceiptWriteOutcome.Done);
    }

    public Task<int> ReprocessAllFailedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = Receipts.Where(r => r.Status == ReceiptStatus.Failed).ToList();
        failed.ForEach(r => r.Reprocess(now));
        return Task.FromResult(failed.Count);
    }

    public Task<ReceiptDeletion> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (ExceptionOnDelete is not null)
        {
            throw ExceptionOnDelete;
        }

        var receipt = Receipts.FirstOrDefault(r => r.Id == id);
        if (receipt is null)
        {
            return Task.FromResult(new ReceiptDeletion(ReceiptWriteOutcome.NotFound));
        }

        if (!receipt.CanBeDeleted)
        {
            return Task.FromResult(new ReceiptDeletion(ReceiptWriteOutcome.Conflict));
        }

        Receipts.Remove(receipt);
        return Task.FromResult(new ReceiptDeletion(ReceiptWriteOutcome.Done, receipt.TransactionNo, receipt.ImagePath));
    }

    public Task<Receipt?> ClaimNextQueuedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var next = Receipts
            .Where(r => r.Status == ReceiptStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefault();
        next?.StartProcessing(now);
        return Task.FromResult(next);
    }

    public Task<int> RequeueInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var processing = Receipts.Where(r => r.Status == ReceiptStatus.Processing).ToList();
        processing.ForEach(r => r.ReturnToQueue(now));
        return Task.FromResult(processing.Count);
    }

    /// <summary>The fake hands out the stored instances, so changes are already saved. This counts the saves.</summary>
    public int Saves { get; private set; }

    public Task SaveAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<bool> TryAddAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        if (ExceptionOnAdd is not null)
        {
            throw ExceptionOnAdd;
        }

        if (ReceiptToAddBeforeNextInsert is { } winner)
        {
            ReceiptToAddBeforeNextInsert = null;
            Assign(winner);
        }

        if (Receipts.Any(r => r.Checksum == receipt.Checksum))
        {
            return Task.FromResult(false);
        }

        Assign(receipt);
        return Task.FromResult(true);
    }

    private void Assign(Receipt receipt)
    {
        typeof(Receipt).GetProperty(nameof(Receipt.TransactionNo))!.SetValue(receipt, ++_lastTransactionNo);
        Receipts.Add(receipt);
    }
}

internal sealed class FakeImageStore : IImageStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var path = $"{Guid.NewGuid():N}";
        Files[path] = content.ToArray();
        return Task.FromResult(path);
    }

    public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) =>
        Files.TryGetValue(path, out var content)
            ? Task.FromResult(content)
            : Task.FromException<byte[]>(new FileNotFoundException("No such stored file.", path));

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }
}
