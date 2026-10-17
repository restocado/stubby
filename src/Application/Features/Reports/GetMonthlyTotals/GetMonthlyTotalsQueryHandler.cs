using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Reports.GetMonthlyTotals;

public sealed class GetMonthlyTotalsQueryHandler(
    IValidator<GetMonthlyTotalsQuery> validator,
    IReportReader reports) : IQueryHandler<GetMonthlyTotalsQuery, GetMonthlyTotalsResponse>
{
    public async ValueTask<GetMonthlyTotalsResponse> Handle(GetMonthlyTotalsQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var data = await reports.GetMonthlyTotalsAsync(query.Year, query.Month, cancellationToken);

        // Always all eight categories, in enum order, so the screen never changes shape.
        var categories = Enum.GetValues<ReceiptCategory>()
            .Select(category =>
            {
                var sum = data.Categories.FirstOrDefault(s => s.Category == category);
                return new CategoryTotal(category, sum?.Total ?? 0m, sum?.Count ?? 0);
            })
            .ToList();

        return new GetMonthlyTotalsResponse(
            query.Year,
            query.Month,
            categories,
            categories.Sum(c => c.Total),
            categories.Sum(c => c.Count),
            data.EarliestMonth,
            data.LatestMonth);
    }
}
