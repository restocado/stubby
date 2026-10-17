using Mediator;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed record ListReceiptsQuery(int Page = 1) : IQuery<ListReceiptsResponse>
{
    public const int PageSize = 25;
}
