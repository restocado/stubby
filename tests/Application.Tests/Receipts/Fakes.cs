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

    public Task<ReceiptPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var items = Receipts
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(new ReceiptPage(items, Receipts.Count));
    }

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
