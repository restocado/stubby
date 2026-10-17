using Mediator;

namespace Stubby.Application.Features.Reports.GetMonthlyTotals;

public sealed record GetMonthlyTotalsQuery(int Year, int Month) : IQuery<GetMonthlyTotalsResponse>;
