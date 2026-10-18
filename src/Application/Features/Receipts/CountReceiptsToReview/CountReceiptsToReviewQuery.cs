using Mediator;

namespace Stubby.Application.Features.Receipts.CountReceiptsToReview;

/// <summary>How many receipts are waiting for review. It feeds the number beside Inbox in the menu.</summary>
public sealed record CountReceiptsToReviewQuery : IQuery<int>;
