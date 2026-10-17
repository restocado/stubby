using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Receipts.ReprocessAllFailedReceipts;

public sealed class ReprocessAllFailedReceiptsCommandHandler(IReceiptRepository receipts, TimeProvider timeProvider)
    : ICommandHandler<ReprocessAllFailedReceiptsCommand, ReprocessAllFailedReceiptsResponse>
{
    public async ValueTask<ReprocessAllFailedReceiptsResponse> Handle(
        ReprocessAllFailedReceiptsCommand command, CancellationToken cancellationToken) =>
        new(await receipts.ReprocessAllFailedAsync(timeProvider.GetUtcNow(), cancellationToken));
}
