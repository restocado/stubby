using FluentValidation;

namespace Stubby.Application.Features.Reports.GetMonthlyTotals;

public sealed class GetMonthlyTotalsQueryValidator : AbstractValidator<GetMonthlyTotalsQuery>
{
    public GetMonthlyTotalsQueryValidator()
    {
        RuleFor(q => q.Month).InclusiveBetween(1, 12);

        // 9998 and not 9999, so that "the month after" is always a date that exists.
        RuleFor(q => q.Year).InclusiveBetween(1, 9998);
    }
}
