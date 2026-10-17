using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Features.Receipts.GetInbox;

/// <param name="ForReview">Oldest first, the order they are reviewed in.</param>
/// <param name="Failed">Newest first.</param>
public sealed record GetInboxResponse(
    IReadOnlyList<InboxItem> ForReview,
    int ForReviewCount,
    IReadOnlyList<InboxItem> Failed,
    int FailedCount,
    int ReadingCount);

/// <param name="Merchant">What was read, which a ForReview receipt has and a Failed one does not.</param>
/// <param name="Error">Why reading failed, for a Failed receipt.</param>
public sealed record InboxItem(
    Guid Id,
    long TransactionNo,
    string OriginalFileName,
    bool IsPdf,
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    IReadOnlyList<FlagReason> FlagReasons,
    string? Error);
