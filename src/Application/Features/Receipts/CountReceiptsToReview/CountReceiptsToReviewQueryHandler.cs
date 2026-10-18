using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Receipts.CountReceiptsToReview;

public sealed class CountReceiptsToReviewQueryHandler(IReceiptRepository receipts)
    : IQueryHandler<CountReceiptsToReviewQuery, int>
{
    public async ValueTask<int> Handle(CountReceiptsToReviewQuery query, CancellationToken cancellationToken) =>
        await receipts.CountForReviewAsync(cancellationToken);
}
