using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Features.Receipts.GetReceiptForReview;

public abstract record GetReceiptForReviewResponse
{
    private GetReceiptForReviewResponse() { }

    public sealed record NotFound : GetReceiptForReviewResponse;

    /// <summary>The receipt is still Queued or Processing, so it cannot be opened yet.</summary>
    public sealed record Locked : GetReceiptForReviewResponse;

    public sealed record Found(ReceiptForReview Receipt) : GetReceiptForReviewResponse;
}

/// <summary>
/// What the review screen shows. ForReview and Failed receipts carry the extracted values (a Failed one has none).
/// An Approved one also carries the final values and category, and a Failed one its last error.
/// </summary>
public sealed record ReceiptForReview(
    Guid Id,
    long TransactionNo,
    ReceiptStatus Status,
    string OriginalFileName,
    string ContentType,
    bool IsFlagged,
    IReadOnlyList<FlagReason> FlagReasons,
    string? ExtractedMerchant,
    DateOnly? ExtractedDate,
    decimal? ExtractedTotal,
    string? Merchant,
    DateOnly? ReceiptDate,
    decimal? Total,
    ReceiptCategory? Category,
    string? LastError);
