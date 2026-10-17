namespace Stubby.Application.Features.Extraction.ProcessNextReceipt;

public abstract record ProcessNextReceiptResult
{
    private ProcessNextReceiptResult() { }

    /// <summary>Nothing was Queued.</summary>
    public sealed record NothingToDo : ProcessNextReceiptResult;

    /// <summary>The receipt is now ForReview, flagged or not.</summary>
    public sealed record Completed(Guid ReceiptId, bool IsFlagged) : ProcessNextReceiptResult;

    /// <summary>The attempt failed. The receipt is Queued again if <paramref name="WillRetry"/>, otherwise Failed.</summary>
    public sealed record Failed(Guid ReceiptId, bool WillRetry) : ProcessNextReceiptResult;

    /// <summary>The app is shutting down. The receipt is Queued again and no attempt was charged.</summary>
    public sealed record Interrupted(Guid ReceiptId) : ProcessNextReceiptResult;
}
