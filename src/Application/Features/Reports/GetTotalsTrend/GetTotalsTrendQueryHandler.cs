using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Reports.GetTotalsTrend;

public sealed class GetTotalsTrendQueryHandler(
    IValidator<GetTotalsTrendQuery> validator,
    IReportReader reports) : IQueryHandler<GetTotalsTrendQuery, GetTotalsTrendResponse>
{
    public async ValueTask<GetTotalsTrendResponse> Handle(GetTotalsTrendQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var last = new YearMonth(query.Year, query.Month);

        // Months before year 1 do not exist, so the window starts no earlier than January of year 1.
        var window = Enumerable.Range(0, GetTotalsTrendQuery.MonthCount)
            .Select(i => last.AddMonths(i - (GetTotalsTrendQuery.MonthCount - 1)))
            .ToList();
        var first = window.Where(m => m.Year >= 1).DefaultIfEmpty(last).First();

        var sums = (await reports.GetMonthlySumsAsync(first, last, cancellationToken)).ToDictionary(s => s.Month, s => s.Total);
        var months = window.Select(m => new MonthTotal(m, sums.GetValueOrDefault(m))).ToList();

        var current = months[^1].Total;
        var previous = months[^2].Total;
        var withReceipts = months.Where(m => sums.ContainsKey(m.Month)).Select(m => m.Total).ToList();

        decimal? change = previous > 0m ? Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero) : null;
        var average = withReceipts.Count > 0 ? Math.Round(withReceipts.Average(), 2, MidpointRounding.AwayFromZero) : 0m;

        return new GetTotalsTrendResponse(months, previous, change, average, withReceipts.Count);
    }
}
