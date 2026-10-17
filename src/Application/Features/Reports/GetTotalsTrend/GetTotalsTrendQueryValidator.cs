using FluentValidation;

namespace Stubby.Application.Features.Reports.GetTotalsTrend;

public sealed class GetTotalsTrendQueryValidator : AbstractValidator<GetTotalsTrendQuery>
{
    public GetTotalsTrendQueryValidator()
    {
        RuleFor(q => q.Month).InclusiveBetween(1, 12);
        RuleFor(q => q.Year).InclusiveBetween(1, 9998);
    }
}
