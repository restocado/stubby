namespace Stubby.Application.Features.Receipts.UploadReceipt;

public abstract record UploadReceiptResponse
{
    private UploadReceiptResponse() { }

    public sealed record Created(Guid Id, long TransactionNo) : UploadReceiptResponse;

    public sealed record UnsupportedType : UploadReceiptResponse;

    public sealed record TooLarge : UploadReceiptResponse;

    public sealed record Empty : UploadReceiptResponse;

    public sealed record Duplicate(long ExistingTransactionNo) : UploadReceiptResponse;
}
