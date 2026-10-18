using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed record ListReceiptsQuery(
    int Page = 1,
    string? Search = null,
    ReceiptStatusFilter Status = ReceiptStatusFilter.All,
    int? Year = null,
    int? Month = null,
    ReceiptCategory? Category = null) : IQuery<ListReceiptsResponse>
{
    public const int PageSize = 25;
    public const int MaxSearchLength = 100;
}
