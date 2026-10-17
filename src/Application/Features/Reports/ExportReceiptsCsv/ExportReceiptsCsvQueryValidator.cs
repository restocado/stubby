using FluentValidation;

namespace Stubby.Application.Features.Reports.ExportReceiptsCsv;

public sealed class ExportReceiptsCsvQueryValidator : AbstractValidator<ExportReceiptsCsvQuery>
{
    public ExportReceiptsCsvQueryValidator()
    {
        RuleFor(q => q)
            .Must(q => q.Year.HasValue == q.Month.HasValue)
            .WithMessage("Give both a year and a month, or neither.");
        RuleFor(q => q.Month!.Value).InclusiveBetween(1, 12).When(q => q.Month.HasValue);
        RuleFor(q => q.Year!.Value).InclusiveBetween(1, 9998).When(q => q.Year.HasValue);
    }
}
