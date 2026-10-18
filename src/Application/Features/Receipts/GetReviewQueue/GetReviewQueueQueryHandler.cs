using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Receipts.GetReviewQueue;

public sealed class GetReviewQueueQueryHandler(IReceiptRepository receipts)
    : IQueryHandler<GetReviewQueueQuery, GetReviewQueueResponse>
{
    public async ValueTask<GetReviewQueueResponse> Handle(GetReviewQueueQuery query, CancellationToken cancellationToken)
    {
        var queue = await receipts.ListForReviewIdsAsync(cancellationToken);

        var index = -1;
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i] == query.CurrentId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return new GetReviewQueueResponse(null, queue.Count, queue.Count > 0 ? queue[0] : null);
        }

        // The review goes round: after the newest comes the oldest again, so a skipped receipt is not lost.
        Guid? next = queue.Count > 1 ? queue[(index + 1) % queue.Count] : null;
        return new GetReviewQueueResponse(index + 1, queue.Count, next);
    }
}
