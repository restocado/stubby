namespace Stubby.Application.Features.Receipts.GetReviewQueue;

/// <param name="Position">
/// The 1-based place of the current receipt, oldest first, or null when it is not waiting for review (it is Failed,
/// Approved or gone).
/// </param>
/// <param name="Count">How many receipts are waiting for review.</param>
/// <param name="NextId">
/// The receipt after the current one, going round to the oldest after the newest, or the oldest when the current one
/// is not in the queue. Null when no other receipt is waiting.
/// </param>
public sealed record GetReviewQueueResponse(int? Position, int Count, Guid? NextId);
