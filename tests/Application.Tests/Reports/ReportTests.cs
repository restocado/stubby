using System.Globalization;
using System.Text;
using FluentValidation;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Reports.ExportReceiptsCsv;
using Stubby.Application.Features.Reports.GetMonthlyTotals;
using Stubby.Application.Tests.Extraction;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Reports;

internal sealed class FakeReportReader : IReportReader
{
    public MonthlyTotalsData Totals { get; set; } = new([], null, null);

    public List<ApprovedReceiptRow> Rows { get; } = [];

    public (int Year, int Month)? TotalsRequestedFor { get; private set; }

    public (int? Year, int? Month)? ListRequestedFor { get; private set; }

    public List<MonthSum> MonthSums { get; } = [];

    public (YearMonth From, YearMonth To)? SumsRequestedFor { get; private set; }

    public Task<IReadOnlyList<MonthSum>> GetMonthlySumsAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        SumsRequestedFor = (from, to);
        return Task.FromResult<IReadOnlyList<MonthSum>>(MonthSums);
    }

    public Task<MonthlyTotalsData> GetMonthlyTotalsAsync(int year, int month, CancellationToken cancellationToken)
    {
        TotalsRequestedFor = (year, month);
        return Task.FromResult(Totals);
    }

    public Task<IReadOnlyList<ApprovedReceiptRow>> ListApprovedAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        ListRequestedFor = (year, month);
        return Task.FromResult<IReadOnlyList<ApprovedReceiptRow>>(Rows);
    }
}

public class MonthlyTotalsTests
{
    private readonly FakeReportReader _reports = new();
    private readonly GetMonthlyTotalsQueryHandler _handler;

    public MonthlyTotalsTests()
    {
        _handler = new GetMonthlyTotalsQueryHandler(new GetMonthlyTotalsQueryValidator(), _reports);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_empty_month_shows_all_eight_categories_at_zero_in_enum_order()
    {
        var response = await _handler.Handle(new GetMonthlyTotalsQuery(2026, 10), Token);

        Assert.Equal(Enum.GetValues<ReceiptCategory>(), response.Categories.Select(c => c.Category));
        Assert.Equal(8, response.Categories.Count);
        Assert.All(response.Categories, c =>
        {
            Assert.Equal(0.00m, c.Total);
            Assert.Equal(0, c.Count);
        });
        Assert.Equal(0.00m, response.GrandTotal);
        Assert.Equal(0, response.GrandCount);
    }

    [Fact]
    public async Task Two_Groceries_receipts_give_their_sum_and_count()
    {
        _reports.Totals = new MonthlyTotalsData([new CategorySum(ReceiptCategory.Groceries, 30.30m, 2)], null, null);

        var response = await _handler.Handle(new GetMonthlyTotalsQuery(2026, 10), Token);

        var groceries = Assert.Single(response.Categories, c => c.Category == ReceiptCategory.Groceries);
        Assert.Equal(30.30m, groceries.Total);
        Assert.Equal(2, groceries.Count);
        Assert.Equal(30.30m, response.GrandTotal);
        Assert.Equal(2, response.GrandCount);
    }

    [Fact]
    public async Task The_grand_total_is_the_exact_sum_of_the_categories()
    {
        _reports.Totals = new MonthlyTotalsData(
            [
                new CategorySum(ReceiptCategory.Groceries, 0.10m, 1),
                new CategorySum(ReceiptCategory.Dining, 0.10m, 1),
                new CategorySum(ReceiptCategory.Other, 0.10m, 1),
            ],
            null,
            null);

        var response = await _handler.Handle(new GetMonthlyTotalsQuery(2026, 10), Token);

        Assert.Equal(0.30m, response.GrandTotal);
        Assert.Equal(response.Categories.Sum(c => c.Total), response.GrandTotal);
        Assert.Equal(3, response.GrandCount);
    }

    [Fact]
    public async Task The_month_asked_for_and_the_range_of_months_come_back()
    {
        _reports.Totals = new MonthlyTotalsData([], new YearMonth(2026, 3), new YearMonth(2026, 11));

        var response = await _handler.Handle(new GetMonthlyTotalsQuery(2026, 10), Token);

        Assert.Equal((2026, 10), _reports.TotalsRequestedFor);
        Assert.Equal((2026, 10), (response.Year, response.Month));
        Assert.Equal(new YearMonth(2026, 3), response.EarliestMonth);
        Assert.Equal(new YearMonth(2026, 11), response.LatestMonth);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, -1)]
    [InlineData(0, 10)]
    [InlineData(9999, 10)]
    public async Task A_month_or_year_out_of_range_fails_validation(int year, int month)
    {
        await Assert.ThrowsAsync<ValidationException>(async () => await _handler.Handle(new GetMonthlyTotalsQuery(year, month), Token));
        Assert.Null(_reports.TotalsRequestedFor);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 12)]
    [InlineData(1, 1)]
    [InlineData(9998, 12)]
    public async Task The_edges_of_the_range_are_valid(int year, int month)
    {
        var response = await _handler.Handle(new GetMonthlyTotalsQuery(year, month), Token);

        Assert.Equal((year, month), (response.Year, response.Month));
    }
}

public class ExportReceiptsCsvTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReportReader _reports = new();
    private readonly ExportReceiptsCsvQueryHandler _handler;

    public ExportReceiptsCsvTests()
    {
        _handler = new ExportReceiptsCsvQueryHandler(new ExportReceiptsCsvQueryValidator(), _reports, new FixedTimeProvider(Now));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ApprovedReceiptRow Row(long number = 42, string merchant = "Fake Cafe", decimal total = 12.50m, ReceiptCategory category = ReceiptCategory.Dining) =>
        new(number, new DateOnly(2026, 10, 2), merchant, category, total);

    private async Task<(byte[] Bytes, string Text, string[] Lines)> ExportAsync(ExportReceiptsCsvQuery? query = null)
    {
        var response = await _handler.Handle(query ?? new ExportReceiptsCsvQuery(), Token);
        var text = new UTF8Encoding(false).GetString(response.Content, 3, response.Content.Length - 3);
        return (response.Content, text, text.Split("\r\n", StringSplitOptions.None));
    }

    [Fact]
    public async Task The_file_has_a_header_and_one_row_for_each_approved_receipt_with_the_columns_in_order()
    {
        _reports.Rows.AddRange([Row(1, "A"), Row(2, "B"), Row(3, "C")]);

        var (_, _, lines) = await ExportAsync();

        Assert.Equal("TransactionNo,Date,Merchant,Category,Total", lines[0]);
        Assert.Equal(3, lines.Skip(1).Count(l => l.Length > 0));
        Assert.Equal("", lines[^1]);
    }

    [Fact]
    public async Task A_row_has_the_transaction_number_date_merchant_category_and_total()
    {
        _reports.Rows.Add(Row(42, "Fake Cafe", 1234.50m, ReceiptCategory.Entertainment));

        var (_, _, lines) = await ExportAsync();

        Assert.Equal("TXN-000042,2026-10-02,Fake Cafe,entertainment,1234.50", lines[1]);
    }

    [Theory]
    [InlineData(1L, "TXN-000001")]
    [InlineData(999_999L, "TXN-999999")]
    [InlineData(1_000_000L, "TXN-1000000")]
    public async Task The_number_is_in_the_same_form_as_on_screen(long number, string expected)
    {
        _reports.Rows.Add(Row(number));

        var (_, _, lines) = await ExportAsync();

        Assert.StartsWith(expected + ",", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_file_starts_with_the_utf8_byte_order_mark_and_uses_commas_and_crlf()
    {
        _reports.Rows.Add(Row());

        var (bytes, text, _) = await ExportAsync();

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_approved_receipts_the_file_is_only_the_header_with_the_byte_order_mark()
    {
        var (bytes, text, _) = await ExportAsync();

        Assert.Equal("TransactionNo,Date,Merchant,Category,Total\r\n", text);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
    }

    [Fact]
    public async Task A_comma_stays_in_one_column_and_a_quote_is_doubled()
    {
        _reports.Rows.AddRange([Row(1, "Smith, Jones & Co"), Row(2, "The \"Corner\" Shop")]);

        var (_, _, lines) = await ExportAsync();

        Assert.Equal("TXN-000001,2026-10-02,\"Smith, Jones & Co\",dining,12.50", lines[1]);
        Assert.Equal("TXN-000002,2026-10-02,\"The \"\"Corner\"\" Shop\",dining,12.50", lines[2]);
    }

    [Fact]
    public async Task A_line_break_in_a_merchant_stays_inside_its_quotes()
    {
        _reports.Rows.Add(Row(1, "First line\r\nSecond line"));

        var (_, text, _) = await ExportAsync();

        Assert.Contains("\"First line\r\nSecond line\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tTabbed", "'\tTabbed")]
    public async Task A_merchant_that_starts_like_a_formula_is_prefixed_so_it_opens_as_text(string merchant, string expected)
    {
        _reports.Rows.Add(Row(1, merchant));

        var (_, _, lines) = await ExportAsync();

        Assert.Equal($"TXN-000001,2026-10-02,{expected},dining,12.50", lines[1]);
    }

    [Fact]
    public async Task A_merchant_with_a_formula_character_that_is_not_first_is_left_alone()
    {
        _reports.Rows.Add(Row(1, "A=B + C-D @ home"));

        var (_, _, lines) = await ExportAsync();

        Assert.Equal("TXN-000001,2026-10-02,A=B + C-D @ home,dining,12.50", lines[1]);
    }

    [Fact]
    public async Task A_defused_merchant_with_a_comma_is_still_quoted()
    {
        _reports.Rows.Add(Row(1, "=A1, B1"));

        var (_, _, lines) = await ExportAsync();

        Assert.Equal("TXN-000001,2026-10-02,\"'=A1, B1\",dining,12.50", lines[1]);
    }

    [Fact]
    public async Task Accents_survive_as_utf8()
    {
        _reports.Rows.Add(Row(1, "Café Müller – Crêperie"));

        var (bytes, text, _) = await ExportAsync();

        Assert.Contains("Café Müller – Crêperie", text, StringComparison.Ordinal);
        Assert.Contains(Encoding.UTF8.GetBytes("é"), Windowed(bytes, 2));
    }

    [Theory]
    [InlineData("1234.50", "1234.50")]
    [InlineData("0", "0.00")]
    [InlineData("-5", "-5.00")]
    [InlineData("0.1", "0.10")]
    [InlineData("9999999999999.99", "9999999999999.99")]
    public async Task A_total_is_a_plain_number_with_two_decimals(string total, string expected)
    {
        _reports.Rows.Add(Row(1, total: decimal.Parse(total, CultureInfo.InvariantCulture)));

        var (_, _, lines) = await ExportAsync();

        Assert.EndsWith("," + expected, lines[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public async Task Dates_and_totals_do_not_depend_on_the_server_culture(string culture)
    {
        _reports.Rows.Add(Row(1, total: 1234.50m));
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var (_, _, lines) = await ExportAsync();

            Assert.Equal("TXN-000001,2026-10-02,Fake Cafe,dining,1234.50", lines[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Every_category_is_exported_in_lowercase()
    {
        foreach (var category in Enum.GetValues<ReceiptCategory>())
        {
            _reports.Rows.Add(Row((long)category + 1, category: category));
        }

        var (_, _, lines) = await ExportAsync();

        Assert.Equal(
            ["groceries", "dining", "transport", "utilities", "shopping", "health", "entertainment", "other"],
            lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[3]));
    }

    [Fact]
    public void A_row_holds_no_receipt_id_file_name_or_path()
    {
        var names = typeof(ApprovedReceiptRow).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["TransactionNo", "Date", "Merchant", "Category", "Total"], names);
    }

    [Fact]
    public async Task The_whole_export_is_named_for_the_day_it_was_made()
    {
        var response = await _handler.Handle(new ExportReceiptsCsvQuery(), Token);

        Assert.Equal("stubby-receipts-20261017.csv", response.FileName);
        Assert.Equal((null, null), _reports.ListRequestedFor);
    }

    [Fact]
    public async Task A_months_export_is_filtered_and_named_for_the_month()
    {
        var response = await _handler.Handle(new ExportReceiptsCsvQuery(2026, 3), Token);

        Assert.Equal("stubby-receipts-2026-03.csv", response.FileName);
        Assert.Equal((2026, 3), _reports.ListRequestedFor);
    }

    [Theory]
    [InlineData(2026, null)]
    [InlineData(null, 3)]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(0, 3)]
    public async Task A_filter_that_is_half_given_or_out_of_range_fails_validation(int? year, int? month)
    {
        await Assert.ThrowsAsync<ValidationException>(async () => await _handler.Handle(new ExportReceiptsCsvQuery(year, month), Token));
        Assert.Null(_reports.ListRequestedFor);
    }

    // The windows of consecutive bytes, so a test can look for a multi-byte character.
    private static IEnumerable<byte[]> Windowed(byte[] bytes, int size) =>
        Enumerable.Range(0, bytes.Length - size + 1).Select(i => bytes[i..(i + size)]);
}
