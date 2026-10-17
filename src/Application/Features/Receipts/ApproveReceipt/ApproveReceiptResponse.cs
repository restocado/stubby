namespace Stubby.Application.Features.Receipts.ApproveReceipt;

public abstract record ApproveReceiptResponse
{
    private ApproveReceiptResponse() { }

    public sealed record Approved(long TransactionNo) : ApproveReceiptResponse;

    public sealed record NotFound : ApproveReceiptResponse;

    /// <summary>The receipt is Queued, Processing or already Approved. Nothing was written.</summary>
    public sealed record Conflict : ApproveReceiptResponse;

    /// <summary>The input was not valid. Nothing was written. Each error names the field it belongs to.</summary>
    public sealed record Invalid(IReadOnlyList<FieldError> Errors) : ApproveReceiptResponse;
}

/// <param name="Field">One of <c>Merchant</c>, <c>Date</c>, <c>Total</c> or <c>Category</c>.</param>
/// <param name="Message">A short message that is safe to show to the user.</param>
public sealed record FieldError(string Field, string Message);
