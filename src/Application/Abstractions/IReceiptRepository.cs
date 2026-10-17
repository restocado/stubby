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

    /// <summary>
    /// One page of receipts, newest first (by <c>CreatedAt</c>, then <c>Id</c>), with the total number of
    /// receipts. <paramref name="page"/> is 1-based. A page past the end has no items.
    /// </summary>
    Task<ReceiptPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the oldest Queued receipt (by <c>CreatedAt</c>) and sets it Processing, in one transaction that
    /// locks the row and skips rows other callers hold. Returns null when nothing is Queued.
    /// </summary>
    Task<Receipt?> ClaimNextQueuedAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Processing receipt to Queued, in one transaction, with its attempts unchanged.
    /// Returns how many there were.
    /// </summary>
    Task<int> RequeueInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the changes made to a receipt that <see cref="ClaimNextQueuedAsync"/> returned. Only the
    /// caller that claimed a Processing receipt may change it, so no concurrency check is made.
    /// </summary>
    Task SaveAsync(Receipt receipt, CancellationToken cancellationToken);
}
