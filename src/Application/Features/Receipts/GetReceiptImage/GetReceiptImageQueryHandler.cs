using Mediator;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.GetReceiptImage;

public sealed class GetReceiptImageQueryHandler(
    IReceiptRepository receipts,
    IImageStore imageStore,
    ILogger<GetReceiptImageQueryHandler> logger) : IQueryHandler<GetReceiptImageQuery, GetReceiptImageResponse>
{
    public async ValueTask<GetReceiptImageResponse> Handle(GetReceiptImageQuery query, CancellationToken cancellationToken)
    {
        var receipt = await receipts.FindByIdAsync(query.Id, cancellationToken);
        if (receipt is null)
        {
            return new GetReceiptImageResponse.NotFound();
        }

        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
        {
            return new GetReceiptImageResponse.Locked();
        }

        try
        {
            // The path comes from the database, never from the caller, and the store refuses one outside its root.
            var content = await imageStore.ReadAsync(receipt.ImagePath, cancellationToken);
            return new GetReceiptImageResponse.Found(content, receipt.ContentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            logger.LogWarning("The stored file of receipt {TransactionNo} is missing.", receipt.TransactionNo);
            return new GetReceiptImageResponse.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "The stored path of receipt {TransactionNo} was refused.", receipt.TransactionNo);
            return new GetReceiptImageResponse.NotFound();
        }
    }
}
