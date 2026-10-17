namespace Stubby.Domain.Receipts;

public enum ReceiptStatus
{
    Queued,
    Processing,
    Failed,
    ForReview,
    Approved,
}
