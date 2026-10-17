namespace Stubby.Application.Features.Receipts.DeleteReceipt;

public abstract record DeleteReceiptResponse
{
    private DeleteReceiptResponse() { }

    public sealed record Deleted(long TransactionNo) : DeleteReceiptResponse;

    public sealed record NotFound : DeleteReceiptResponse;

    /// <summary>The receipt is Queued or Processing, so it is being read. Nothing was removed.</summary>
    public sealed record Conflict : DeleteReceiptResponse;
}
