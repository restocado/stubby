namespace Stubby.Application.Features.Receipts.GetReceiptImage;

public abstract record GetReceiptImageResponse
{
    private GetReceiptImageResponse() { }

    /// <summary>There is no such receipt, or its file is missing or not where the store would serve it from.</summary>
    public sealed record NotFound : GetReceiptImageResponse;

    /// <summary>The receipt is still Queued or Processing, so it cannot be opened yet.</summary>
    public sealed record Locked : GetReceiptImageResponse;

    public sealed record Found(byte[] Content, string ContentType) : GetReceiptImageResponse;
}
