using FluentValidation;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed class ListReceiptsQueryValidator : AbstractValidator<ListReceiptsQuery>
{
    public ListReceiptsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.Search).MaximumLength(ListReceiptsQuery.MaxSearchLength);

        // A month is only meaningful with its year, and a year with its month.
        RuleFor(q => q.Month).InclusiveBetween(1, 12).When(q => q.Month is not null);
        RuleFor(q => q.Year).InclusiveBetween(1, 9998).When(q => q.Year is not null);
        RuleFor(q => q.Month).NotNull().When(q => q.Year is not null);
        RuleFor(q => q.Year).NotNull().When(q => q.Month is not null);
    }
}
