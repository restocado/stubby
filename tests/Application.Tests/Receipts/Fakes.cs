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

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }
}
