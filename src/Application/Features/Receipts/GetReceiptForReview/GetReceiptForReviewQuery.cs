using Mediator;

namespace Stubby.Application.Features.Receipts.GetReceiptForReview;

public sealed record GetReceiptForReviewQuery(Guid Id) : IQuery<GetReceiptForReviewResponse>;
