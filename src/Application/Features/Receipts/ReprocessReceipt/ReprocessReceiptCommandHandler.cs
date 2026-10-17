using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Receipts.ReprocessReceipt;

public sealed class ReprocessReceiptCommandHandler(IReceiptRepository receipts, TimeProvider timeProvider)
    : ICommandHandler<ReprocessReceiptCommand, ReprocessReceiptResponse>
{
    public async ValueTask<ReprocessReceiptResponse> Handle(ReprocessReceiptCommand command, CancellationToken cancellationToken)
    {
        var outcome = await receipts.ReprocessAsync(command.ReceiptId, timeProvider.GetUtcNow(), cancellationToken);
        return outcome switch
        {
            ReceiptWriteOutcome.Done => new ReprocessReceiptResponse.Requeued(),
            ReceiptWriteOutcome.NotFound => new ReprocessReceiptResponse.NotFound(),
            _ => new ReprocessReceiptResponse.Conflict(),
        };
    }
}
