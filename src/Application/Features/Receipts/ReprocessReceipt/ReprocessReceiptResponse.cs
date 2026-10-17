namespace Stubby.Application.Features.Receipts.ReprocessReceipt;

public abstract record ReprocessReceiptResponse
{
    private ReprocessReceiptResponse() { }

    public sealed record Requeued : ReprocessReceiptResponse;

    public sealed record NotFound : ReprocessReceiptResponse;

    /// <summary>The receipt is not Failed. Nothing was changed.</summary>
    public sealed record Conflict : ReprocessReceiptResponse;
}
