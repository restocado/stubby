using Stubby.Domain.Receipts;

namespace Stubby.Application.Abstractions;

/// <summary>
/// What needs attention. <see cref="ForReview"/> is oldest first (by <c>CreatedAt</c>) and <see cref="Failed"/> newest
/// first, each cut to the limit asked for, with the full counts beside them.
/// </summary>
public sealed record InboxData(
    IReadOnlyList<Receipt> ForReview,
    int ForReviewCount,
    IReadOnlyList<Receipt> Failed,
    int FailedCount,
    int ReadingCount);
