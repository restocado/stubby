using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed class ListReceiptsQueryHandler(
    IValidator<ListReceiptsQuery> validator,
    IReceiptRepository receipts) : IQueryHandler<ListReceiptsQuery, ListReceiptsResponse>
{
    public async ValueTask<ListReceiptsResponse> Handle(ListReceiptsQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var filter = new ReceiptFilter(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Status,
            query.Year,
            query.Month,
            query.Category);
        var page = await receipts.ListAsync(filter, query.Page, ListReceiptsQuery.PageSize, cancellationToken);
        var pageCount = (page.TotalCount + ListReceiptsQuery.PageSize - 1) / ListReceiptsQuery.PageSize;
        return new ListReceiptsResponse(page.Items.Select(ToItem).ToList(), page.TotalCount, pageCount);
    }

    private static ReceiptListItem ToItem(Receipt receipt)
    {
        // Approved receipts show the final values and ForReview ones what was extracted. The rest have nothing yet.
        var (merchant, date, total) = receipt.Status switch
        {
            ReceiptStatus.Approved => (receipt.Merchant, receipt.ReceiptDate, receipt.Total),
            ReceiptStatus.ForReview => (receipt.ExtractedMerchant, receipt.ExtractedDate, receipt.ExtractedTotal),
            _ => ((string?)null, (DateOnly?)null, (decimal?)null),
        };

        return new ReceiptListItem(
            receipt.Id,
            receipt.TransactionNo,
            receipt.OriginalFileName,
            receipt.Status,
            receipt.IsFlagged,
            receipt.CreatedAt,
            merchant,
            date,
            total,
            receipt.Status == ReceiptStatus.Approved ? receipt.Category : null,
            receipt.CanBeOpened,
            receipt.CanBeDeleted);
    }
}
