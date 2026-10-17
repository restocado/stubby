using FluentValidation;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.UploadReceipt;

public sealed class UploadReceiptCommandValidator : AbstractValidator<UploadReceiptCommand>
{
    public const string UnsupportedTypeCode = "UnsupportedType";
    public const string EmptyCode = "Empty";
    public const string TooLargeCode = "TooLarge";

    public UploadReceiptCommandValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.ContentType)
            .Must(UploadFileTypes.IsAllowedContentType)
            .WithErrorCode(UnsupportedTypeCode);
        RuleFor(c => c)
            .Must(c => UploadFileTypes.ExtensionMatches(c.ContentType, c.FileName))
            .WithErrorCode(UnsupportedTypeCode);
        RuleFor(c => c.Size)
            .GreaterThan(0)
            .WithErrorCode(EmptyCode);
        RuleFor(c => c.Size)
            .LessThanOrEqualTo(Receipt.MaxFileSizeBytes)
            .WithErrorCode(TooLargeCode);
    }
}
