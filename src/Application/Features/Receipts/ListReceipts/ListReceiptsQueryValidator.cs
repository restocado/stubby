using FluentValidation;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed class ListReceiptsQueryValidator : AbstractValidator<ListReceiptsQuery>
{
    public ListReceiptsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
    }
}
