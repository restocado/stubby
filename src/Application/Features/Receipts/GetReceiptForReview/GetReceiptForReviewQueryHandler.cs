using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Features.Receipts.GetReceiptForReview;

public sealed class GetReceiptForReviewQueryHandler(IReceiptRepository receipts)
    : IQueryHandler<GetReceiptForReviewQuery, GetReceiptForReviewResponse>
{
    public async ValueTask<GetReceiptForReviewResponse> Handle(GetReceiptForReviewQuery query, CancellationToken cancellationToken)
    {
        var receipt = await receipts.FindByIdAsync(query.Id, cancellationToken);
        if (receipt is null)
        {
            return new GetReceiptForReviewResponse.NotFound();
        }

        // Queued and Processing receipts are being read, so they are locked.
        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
        {
            return new GetReceiptForReviewResponse.Locked();
        }

        var approved = receipt.Status == ReceiptStatus.Approved;
        return new GetReceiptForReviewResponse.Found(new ReceiptForReview(
            receipt.Id,
            receipt.TransactionNo,
            receipt.Status,
            receipt.OriginalFileName,
            receipt.ContentType,
            receipt.IsFlagged,
            ParseFlagReasons(receipt.FlagReasons),
            receipt.ExtractedMerchant,
            receipt.ExtractedDate,
            receipt.ExtractedTotal,
            approved ? receipt.Merchant : null,
            approved ? receipt.ReceiptDate : null,
            approved ? receipt.Total : null,
            approved ? receipt.Category : null,
            receipt.Status == ReceiptStatus.Failed ? receipt.LastError : null));
    }

    // FlagReasons is stored as comma-separated names. A name this version does not know is skipped.
    private static List<FlagReason> ParseFlagReasons(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => Enum.TryParse<FlagReason>(name, out var reason) ? (FlagReason?)reason : null)
                .OfType<FlagReason>()
                .ToList();
}
