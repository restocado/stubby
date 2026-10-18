using FluentValidation;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Reports.GetTotalsTrend;

namespace Stubby.Application.Tests.Reports;

public class TotalsTrendTests
{
    private readonly FakeReportReader _reports = new();
    private readonly GetTotalsTrendQueryHandler _handler;

    public TotalsTrendTests()
    {
        _handler = new GetTotalsTrendQueryHandler(new GetTotalsTrendQueryValidator(), _reports);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<GetTotalsTrendResponse> Trend(int year, int month) =>
        _handler.Handle(new GetTotalsTrendQuery(year, month), Token).AsTask();

    [Fact]
    public async Task The_window_is_twelve_months_oldest_first_ending_with_the_month_asked_for()
    {
        var trend = await Trend(2026, 3);

        Assert.Equal(12, trend.Months.Count);
        Assert.Equal(new YearMonth(2025, 4), trend.Months[0].Month);
        Assert.Equal(new YearMonth(2026, 3), trend.Months[^1].Month);
        Assert.Equal((new YearMonth(2025, 4), new YearMonth(2026, 3)), _reports.SumsRequestedFor);
    }

    [Fact]
    public async Task A_month_with_no_receipts_is_zero()
    {
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 3), 100m));

        var trend = await Trend(2026, 3);

        Assert.Equal(100m, trend.Months[^1].Total);
        Assert.All(trend.Months.Take(11), m => Assert.Equal(0m, m.Total));
    }

    [Fact]
    public async Task The_change_is_the_percent_above_the_month_before()
    {
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 2), 256.80m));
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 3), 288.42m));

        var trend = await Trend(2026, 3);

        Assert.Equal(256.80m, trend.PreviousMonthTotal);
        Assert.Equal(12.3m, trend.ChangePercent);
    }

    [Fact]
    public async Task A_fall_is_a_negative_change()
    {
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 2), 200m));
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 3), 150m));

        var trend = await Trend(2026, 3);

        Assert.Equal(-25m, trend.ChangePercent);
    }

    [Fact]
    public async Task With_nothing_in_the_month_before_there_is_no_change_to_show()
    {
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 3), 288.42m));

        var trend = await Trend(2026, 3);

        Assert.Equal(0m, trend.PreviousMonthTotal);
        Assert.Null(trend.ChangePercent);
    }

    [Fact]
    public async Task The_average_is_over_the_months_that_have_receipts_only()
    {
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2025, 12), 100m));
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 1), 200m));
        _reports.MonthSums.Add(new MonthSum(new YearMonth(2026, 3), 301m));

        var trend = await Trend(2026, 3);

        Assert.Equal(3, trend.MonthsWithReceipts);
        Assert.Equal(200.33m, trend.AveragePerMonth);
    }

    [Fact]
    public async Task With_no_receipts_at_all_the_average_is_zero()
    {
        var trend = await Trend(2026, 3);

        Assert.Equal(0, trend.MonthsWithReceipts);
        Assert.Equal(0m, trend.AveragePerMonth);
    }

    [Fact]
    public async Task The_window_crosses_the_new_year()
    {
        var trend = await Trend(2026, 1);

        Assert.Equal(new YearMonth(2025, 2), trend.Months[0].Month);
        Assert.Equal(new YearMonth(2025, 12), trend.Months[10].Month);
    }

    [Fact]
    public async Task The_first_year_does_not_ask_for_months_that_do_not_exist()
    {
        var trend = await Trend(1, 3);

        Assert.Equal(12, trend.Months.Count);
        Assert.Equal(new YearMonth(1, 1), _reports.SumsRequestedFor!.Value.From);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 5)]
    [InlineData(9999, 5)]
    public async Task A_month_or_year_that_does_not_exist_fails_validation(int year, int month)
    {
        await Assert.ThrowsAsync<ValidationException>(async () => await Trend(year, month));
    }
}
