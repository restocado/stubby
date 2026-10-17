using FluentValidation;

namespace Stubby.Application.Features.Receipts.ApproveReceipt;

/// <summary>
/// Only a missing or malformed value blocks an approval. A total of zero or less, a date in the future and a very
/// old date are allowed: the flag is advice and the user has the last word.
/// </summary>
public sealed class ApproveReceiptCommandValidator : AbstractValidator<ApproveReceiptCommand>
{
    // The largest value that fits the numeric(15,2) column.
    private const decimal MaxTotal = 9_999_999_999_999.99m;

    public ApproveReceiptCommandValidator()
    {
        RuleFor(c => c.Merchant)
            .Must(merchant => !string.IsNullOrWhiteSpace(merchant))
            .WithMessage("Enter the merchant.");

        RuleFor(c => c.Date)
            .NotNull()
            .WithMessage("Enter the date.");

        RuleFor(c => c.Total)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Enter the total.")
            .Must(total => decimal.Round(total!.Value, 2) == total.Value).WithMessage("The total can have at most two decimals.")
            .Must(total => Math.Abs(total!.Value) <= MaxTotal).WithMessage("The total is too large.");

        RuleFor(c => c.Category)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Choose a category.")
            .IsInEnum().WithMessage("Choose a category.");
    }
}
