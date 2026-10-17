using Mediator;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Features.Extraction.ProcessNextReceipt;

public sealed class ProcessNextReceiptCommandHandler(
    IReceiptRepository receipts,
    IImageStore imageStore,
    IReceiptExtractor extractor,
    IExtractionSettings settings,
    TimeProvider timeProvider,
    ILogger<ProcessNextReceiptCommandHandler> logger) : ICommandHandler<ProcessNextReceiptCommand, ProcessNextReceiptResult>
{
    public async ValueTask<ProcessNextReceiptResult> Handle(ProcessNextReceiptCommand command, CancellationToken cancellationToken)
    {
        // Settings are read for every receipt, and before one is claimed, so a bad value cannot strand a Processing receipt.
        var maxAttempts = settings.MaxAttempts;
        var flagTotalLimit = settings.FlagTotalLimit;

        var receipt = await receipts.ClaimNextQueuedAsync(timeProvider.GetUtcNow(), cancellationToken);
        if (receipt is null)
        {
            return new ProcessNextReceiptResult.NothingToDo();
        }

        var (extracted, failure, interrupted) = await ExtractAsync(receipt, cancellationToken);

        // The outcome is saved with no cancellation token: it must be recorded even while the app is shutting down.
        if (interrupted)
        {
            receipt.ReturnToQueue(timeProvider.GetUtcNow());
            await receipts.SaveAsync(receipt, CancellationToken.None);
            return new ProcessNextReceiptResult.Interrupted(receipt.Id);
        }

        if (extracted is null)
        {
            receipt.FailAttempt(failure!, maxAttempts, timeProvider.GetUtcNow());
            await receipts.SaveAsync(receipt, CancellationToken.None);
            logger.LogWarning(
                "Receipt {TransactionNo} failed attempt {Attempt} of {MaxAttempts}: {Reason}",
                receipt.TransactionNo, receipt.AttemptsUsed, maxAttempts, failure);
            return new ProcessNextReceiptResult.Failed(receipt.Id, WillRetry: receipt.Status == ReceiptStatus.Queued);
        }

        // "Today" is the server's local date. Subtotal and tax are used here and then dropped: they are never stored.
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var flags = ReceiptFlagEvaluator.Evaluate(
            extracted.Total, extracted.Subtotal, extracted.Tax, extracted.Date, flagTotalLimit, today);
        receipt.CompleteExtraction(
            extracted.Merchant,
            extracted.Date,
            extracted.Total,
            extracted.Model,
            extracted.PromptVersion,
            flags.IsFlagged,
            flags.ReasonCodes,
            timeProvider.GetUtcNow());
        await receipts.SaveAsync(receipt, CancellationToken.None);
        logger.LogInformation(
            "Receipt {TransactionNo} is ready for review. Flagged: {IsFlagged}.", receipt.TransactionNo, flags.IsFlagged);
        return new ProcessNextReceiptResult.Completed(receipt.Id, flags.IsFlagged);
    }

    // Any failure here is one failed attempt on this receipt and never an exception for the worker.
    // The reasons are fixed or come from ExtractionException, so they never hold receipt content.
    private async Task<(ExtractionResult? Result, string? Failure, bool Interrupted)> ExtractAsync(
        Receipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            var content = await imageStore.ReadAsync(receipt.ImagePath, cancellationToken);
            var result = await extractor.ExtractAsync(new ReceiptFile(content, receipt.ContentType), cancellationToken);
            return (result, null, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (null, null, true);
        }
        catch (ExtractionException ex)
        {
            return (null, ex.Message, false);
        }
        catch (FileNotFoundException)
        {
            return (null, "The stored file is missing.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "The stored file could not be read.", false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while reading receipt {TransactionNo}.", receipt.TransactionNo);
            return (null, "An unexpected error occurred.", false);
        }
    }
}
