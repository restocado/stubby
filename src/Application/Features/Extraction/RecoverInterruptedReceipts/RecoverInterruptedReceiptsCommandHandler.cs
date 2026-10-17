using Mediator;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Extraction.RecoverInterruptedReceipts;

public sealed class RecoverInterruptedReceiptsCommandHandler(
    IReceiptRepository receipts,
    TimeProvider timeProvider,
    ILogger<RecoverInterruptedReceiptsCommandHandler> logger) : ICommandHandler<RecoverInterruptedReceiptsCommand, int>
{
    public async ValueTask<int> Handle(RecoverInterruptedReceiptsCommand command, CancellationToken cancellationToken)
    {
        var count = await receipts.RequeueInterruptedAsync(timeProvider.GetUtcNow(), cancellationToken);
        logger.LogInformation("Returned {Count} interrupted receipts to the queue.", count);
        return count;
    }
}
