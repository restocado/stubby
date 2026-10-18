using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.GetInbox;

public sealed class GetInboxQueryHandler(IReceiptRepository receipts) : IQueryHandler<GetInboxQuery, GetInboxResponse>
{
    public async ValueTask<GetInboxResponse> Handle(GetInboxQuery query, CancellationToken cancellationToken)
    {
        var inbox = await receipts.GetInboxAsync(GetInboxQuery.Limit, cancellationToken);
        return new GetInboxResponse(
            inbox.ForReview.Select(ToItem).ToList(),
            inbox.ForReviewCount,
            inbox.Failed.Select(ToItem).ToList(),
            inbox.FailedCount,
            inbox.ReadingCount);
    }

    private static InboxItem ToItem(Receipt receipt) => new(
        receipt.Id,
        receipt.TransactionNo,
        receipt.OriginalFileName,
        receipt.ContentType == "application/pdf",
        receipt.ExtractedMerchant,
        receipt.ExtractedDate,
        receipt.ExtractedTotal,
        FlagReasonParser.Parse(receipt.FlagReasons),
        receipt.Status == ReceiptStatus.Failed ? receipt.LastError : null);
}
