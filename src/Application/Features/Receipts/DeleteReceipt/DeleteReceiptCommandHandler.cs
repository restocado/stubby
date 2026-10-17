using Mediator;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Receipts.DeleteReceipt;

public sealed class DeleteReceiptCommandHandler(
    IReceiptRepository receipts,
    IImageStore imageStore,
    ILogger<DeleteReceiptCommandHandler> logger) : ICommandHandler<DeleteReceiptCommand, DeleteReceiptResponse>
{
    public async ValueTask<DeleteReceiptResponse> Handle(DeleteReceiptCommand command, CancellationToken cancellationToken)
    {
        var deletion = await receipts.DeleteAsync(command.ReceiptId, cancellationToken);
        if (deletion.Outcome != ReceiptWriteOutcome.Done)
        {
            return deletion.Outcome == ReceiptWriteOutcome.NotFound
                ? new DeleteReceiptResponse.NotFound()
                : new DeleteReceiptResponse.Conflict();
        }

        // The row is already gone, so the delete has succeeded. A file that cannot be removed is harmless,
        // because no row points at it, and is only logged. Nothing about the receipt is in the log line.
        try
        {
            await imageStore.DeleteAsync(deletion.ImagePath!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The file of deleted receipt {TransactionNo} could not be removed.", deletion.TransactionNo);
        }

        return new DeleteReceiptResponse.Deleted(deletion.TransactionNo);
    }
}
