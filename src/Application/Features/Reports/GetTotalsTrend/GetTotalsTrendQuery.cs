using Mediator;

namespace Stubby.Application.Features.Reports.GetTotalsTrend;

/// <summary>The approved total of each of the 12 months that end with the given month, and how the last compares with the one before.</summary>
public sealed record GetTotalsTrendQuery(int Year, int Month) : IQuery<GetTotalsTrendResponse>
{
    public const int MonthCount = 12;
}
