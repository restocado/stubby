namespace Stubby.Application.Features.Receipts.ReprocessAllFailedReceipts;

/// <param name="RequeuedCount">How many receipts were put back in the queue. Zero is not an error.</param>
public sealed record ReprocessAllFailedReceiptsResponse(int RequeuedCount);
