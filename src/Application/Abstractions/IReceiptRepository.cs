using Stubby.Domain.Receipts;

namespace Stubby.Application.Abstractions;

public interface IReceiptRepository
{
    Task<long?> FindTransactionNoByChecksumAsync(string checksum, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new receipt. Returns false, saving nothing, when a receipt with the same checksum
    /// already exists. On success the receipt carries the transaction number the database assigned.
    /// </summary>
    Task<bool> TryAddAsync(Receipt receipt, CancellationToken cancellationToken);
}
