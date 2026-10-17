using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.ApproveReceipt;

public sealed class ApproveReceiptCommandHandler(
    IValidator<ApproveReceiptCommand> validator,
    IReceiptRepository receipts,
    TimeProvider timeProvider) : ICommandHandler<ApproveReceiptCommand, ApproveReceiptResponse>
{
    public async ValueTask<ApproveReceiptResponse> Handle(ApproveReceiptCommand command, CancellationToken cancellationToken)
    {
        // An unknown id and a receipt in the wrong state come before the input is looked at.
        var existing = await receipts.FindByIdAsync(command.ReceiptId, cancellationToken);
        if (existing is null)
        {
            return new ApproveReceiptResponse.NotFound();
        }

        if (!CanBeApproved(existing))
        {
            return new ApproveReceiptResponse.Conflict();
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new ApproveReceiptResponse.Invalid(
                validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)).ToList());
        }

        // The state is checked again inside the transaction, with the row locked: another tab may have
        // approved or deleted the receipt since it was read above.
        var now = timeProvider.GetUtcNow();
        var outcome = await receipts.ApproveAsync(
            command.ReceiptId,
            receipt => CanBeApproved(receipt)
                ? receipt.Approve(command.Merchant, command.Date, command.Total, command.Category!.Value, now)
                : null,
            cancellationToken);

        return outcome switch
        {
            ReceiptWriteOutcome.Done => new ApproveReceiptResponse.Approved(existing.TransactionNo),
            ReceiptWriteOutcome.NotFound => new ApproveReceiptResponse.NotFound(),
            _ => new ApproveReceiptResponse.Conflict(),
        };
    }

    private static bool CanBeApproved(Receipt receipt) => receipt.Status is ReceiptStatus.ForReview or ReceiptStatus.Failed;
}
