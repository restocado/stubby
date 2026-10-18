using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Application.Features.Receipts.DeleteReceipt;
using Stubby.Application.Features.Reports.ExportReceiptsCsv;
using Stubby.Application.Features.Reports.GetMonthlyTotals;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>The reports through the real handlers, queries and database, and over HTTP.</summary>
public class ReportsTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(query, Token));

    private Task<GetMonthlyTotalsResponse> TotalsAsync(int year, int month) => SendAsync(new GetMonthlyTotalsQuery(year, month));

    /// <summary>Uploads a receipt, has the worker step read it, and approves it with these final values.</summary>
    private async Task<Guid> ApprovedAsync(
        DateOnly date, decimal total, ReceiptCategory category, string merchant = "Fake Cafe")
    {
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        var response = await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, merchant, date, total, category), Token));
        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        return id;
    }

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private async Task<string[][]> CsvRowsAsync(int? year = null, int? month = null)
    {
        var export = await SendAsync(new ExportReceiptsCsvQuery(year, month));
        var text = new UTF8Encoding(false).GetString(export.Content, 3, export.Content.Length - 3);
        return [.. text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split(','))];
    }

    [Fact]
    public async Task Each_months_totals_match_sums_worked_out_independently()
    {
        var september = new[]
        {
            (new DateOnly(2026, 9, 3), 10.10m, ReceiptCategory.Groceries),
            (new DateOnly(2026, 9, 14), 20.20m, ReceiptCategory.Groceries),
            (new DateOnly(2026, 9, 30), 15.55m, ReceiptCategory.Dining),
        };
        var october = new[]
        {
            (new DateOnly(2026, 10, 1), 7.25m, ReceiptCategory.Transport),
            (new DateOnly(2026, 10, 15), 3.30m, ReceiptCategory.Dining),
            (new DateOnly(2026, 10, 31), 1.01m, ReceiptCategory.Groceries),
        };
        foreach (var (date, total, category) in september.Concat(october))
        {
            await ApprovedAsync(date, total, category);
        }

        foreach (var (month, expected) in new[] { (9, september), (10, october) })
        {
            var totals = await TotalsAsync(2026, month);

            foreach (var category in Enum.GetValues<ReceiptCategory>())
            {
                var mine = expected.Where(e => e.Item3 == category).ToList();
                var row = totals.Categories.Single(c => c.Category == category);
                Assert.Equal(mine.Sum(e => e.Item2), row.Total);
                Assert.Equal(mine.Count, row.Count);
            }

            Assert.Equal(expected.Sum(e => e.Item2), totals.GrandTotal);
            Assert.Equal(3, totals.GrandCount);
        }

        Assert.Equal(30.30m, (await TotalsAsync(2026, 9)).Categories.Single(c => c.Category == ReceiptCategory.Groceries).Total);
    }

    [Fact]
    public async Task The_csv_total_for_a_month_equals_the_grand_total_on_the_totals_page()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 10.10m, ReceiptCategory.Groceries);
        await ApprovedAsync(new DateOnly(2026, 10, 9), 0.10m, ReceiptCategory.Dining);
        await ApprovedAsync(new DateOnly(2026, 10, 20), 0.10m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 25), 0.10m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 9, 25), 99.99m, ReceiptCategory.Other);

        var rows = await CsvRowsAsync(2026, 10);

        var totals = await TotalsAsync(2026, 10);
        Assert.Equal(4, rows.Length);
        Assert.Equal(totals.GrandTotal, rows.Sum(r => decimal.Parse(r[4], CultureInfo.InvariantCulture)));
        Assert.Equal(10.40m, totals.GrandTotal);
    }

    [Fact]
    public async Task A_receipt_is_in_neither_report_until_approved_then_in_both_then_in_neither_after_a_delete()
    {
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        var date = new DateOnly(2026, 10, 2);
        Assert.Equal(0, (await TotalsAsync(2026, 10)).GrandCount);
        Assert.Empty(await CsvRowsAsync());

        await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, "Fake Cafe", date, 12.50m, ReceiptCategory.Dining), Token));
        Assert.Equal(12.50m, (await TotalsAsync(2026, 10)).GrandTotal);
        Assert.Single(await CsvRowsAsync());

        await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(new DeleteReceiptCommand(id), Token));
        Assert.Equal(0m, (await TotalsAsync(2026, 10)).GrandTotal);
        Assert.Empty(await CsvRowsAsync());
        Assert.Equal(3, await Fixture.WithDbAsync(db => db.FieldRecords.CountAsync(Token)));
    }

    [Fact]
    public async Task Receipts_that_are_not_Approved_add_nothing()
    {
        var date = new DateOnly(2026, 10, 2);
        Fixture.Extractor.Result = StubExtractor.Sample with { Date = date, Total = 50m };
        await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());
        Fixture.Extractor.Failure = null;
        await UploadAsync();
        var processing = await UploadAsync();
        await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));

        var totals = await TotalsAsync(2026, 10);

        Assert.Equal(0m, totals.GrandTotal);
        Assert.Equal(0, totals.GrandCount);
        Assert.Empty(await CsvRowsAsync());
        Assert.NotEqual(Guid.Empty, processing);
    }

    [Fact]
    public async Task A_manually_entered_Failed_receipt_counts_like_any_other()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());

        await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, "Typed Cafe", new DateOnly(2026, 10, 2), 8.25m, ReceiptCategory.Groceries), Token));

        Assert.True((await Fixture.ReloadAsync(id)).IsManualEntry);
        var totals = await TotalsAsync(2026, 10);
        Assert.Equal(8.25m, totals.Categories.Single(c => c.Category == ReceiptCategory.Groceries).Total);
        Assert.Single(await CsvRowsAsync());
    }

    [Fact]
    public async Task The_month_edges_are_inclusive_at_the_start_and_end_and_exclusive_either_side()
    {
        await ApprovedAsync(new DateOnly(2026, 9, 30), 1m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 1), 2m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 31), 4m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 11, 1), 8m, ReceiptCategory.Other);

        Assert.Equal(6m, (await TotalsAsync(2026, 10)).GrandTotal);
        Assert.Equal(1m, (await TotalsAsync(2026, 9)).GrandTotal);
        Assert.Equal(8m, (await TotalsAsync(2026, 11)).GrandTotal);
        Assert.Equal(2, (await CsvRowsAsync(2026, 10)).Length);
    }

    [Fact]
    public async Task A_receipt_counts_in_the_month_of_its_date_and_not_the_month_it_was_uploaded()
    {
        // Uploaded and approved now, but dated years ago.
        await ApprovedAsync(new DateOnly(2019, 3, 15), 42m, ReceiptCategory.Health);

        Assert.Equal(42m, (await TotalsAsync(2019, 3)).GrandTotal);
        var now = DateTime.Now;
        Assert.Equal(0m, (await TotalsAsync(now.Year, now.Month)).GrandTotal);
    }

    [Fact]
    public async Task The_range_of_months_runs_from_the_first_approved_receipt_to_the_last()
    {
        Assert.Null((await TotalsAsync(2026, 10)).EarliestMonth);
        await ApprovedAsync(new DateOnly(2026, 3, 9), 1m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2027, 1, 20), 1m, ReceiptCategory.Other);

        var totals = await TotalsAsync(2026, 10);

        Assert.Equal(new YearMonth(2026, 3), totals.EarliestMonth);
        Assert.Equal(new YearMonth(2027, 1), totals.LatestMonth);
    }

    [Fact]
    public async Task The_largest_amount_the_column_holds_round_trips_through_the_totals_and_the_csv()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 9_999_999_999_999.99m, ReceiptCategory.Shopping);

        var totals = await TotalsAsync(2026, 10);
        var rows = await CsvRowsAsync();

        Assert.Equal(9_999_999_999_999.99m, totals.GrandTotal);
        Assert.Equal("9999999999999.99", Assert.Single(rows)[4]);
    }

    [Fact]
    public async Task A_thousand_receipts_sum_to_the_exact_expected_amounts()
    {
        await SeedApprovedAsync(1000);
        var expected = ExpectedSeed(1000);

        var totals = await TotalsAsync(2026, 10);

        foreach (var category in Enum.GetValues<ReceiptCategory>())
        {
            var row = totals.Categories.Single(c => c.Category == category);
            Assert.Equal(expected.Where(e => e.Category == category).Sum(e => e.Total), row.Total);
            Assert.Equal(expected.Count(e => e.Category == category), row.Count);
        }

        Assert.Equal(expected.Sum(e => e.Total), totals.GrandTotal);
        Assert.Equal(1000, totals.GrandCount);
        Assert.Equal(1000, (await CsvRowsAsync()).Length);
    }

    [Fact]
    public async Task The_csv_is_ordered_by_date_and_then_by_when_the_receipt_was_created()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 20), 1m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 5), 2m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 5), 3m, ReceiptCategory.Other);

        var rows = await CsvRowsAsync();

        Assert.Equal(["2026-10-05", "2026-10-05", "2026-10-20"], rows.Select(r => r[1]));
        Assert.Equal(["2.00", "3.00", "1.00"], rows.Select(r => r[4]));
    }

    // ----- Over HTTP, with the real app -----

    private HttpClient Client => Fixture.CreateWebClient();

    [Fact]
    public async Task The_csv_download_has_the_byte_order_mark_the_right_type_and_a_dated_file_name()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 1234.50m, ReceiptCategory.Dining, "Café Müller");

        var response = await Client.GetAsync("/api/reports/receipts.csv", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Matches(@"^stubby-receipts-\d{8}\.csv$", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("TransactionNo,Date,Merchant,Category,Total\r\n", text, StringComparison.Ordinal);
        Assert.Contains("TXN-000001,2026-10-02,Café Müller,dining,1234.50\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_csv_download_can_be_filtered_to_one_month_and_is_named_for_it()
    {
        await ApprovedAsync(new DateOnly(2026, 9, 2), 1m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2026, 10, 2), 2m, ReceiptCategory.Other);

        var response = await Client.GetAsync("/api/reports/receipts.csv?year=2026&month=10", Token);

        Assert.Equal("stubby-receipts-2026-10.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("2026-10-02", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-09-02", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_approved_receipts_the_download_succeeds_with_only_the_header()
    {
        var response = await Client.GetAsync("/api/reports/receipts.csv", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal("TransactionNo,Date,Merchant,Category,Total\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Theory]
    [InlineData("year=2026")]
    [InlineData("month=10")]
    [InlineData("year=2026&month=13")]
    [InlineData("year=2026&month=0")]
    [InlineData("year=0&month=1")]
    public async Task A_half_given_or_impossible_month_is_a_bad_request(string query)
    {
        var response = await Client.GetAsync($"/api/reports/receipts.csv?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_totals_page_shows_each_category_with_receipts_with_its_sum_and_the_grand_total()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 10.10m, ReceiptCategory.Groceries);
        await ApprovedAsync(new DateOnly(2026, 10, 9), 20.20m, ReceiptCategory.Groceries);
        await ApprovedAsync(new DateOnly(2026, 10, 12), 5.00m, ReceiptCategory.Dining);

        var html = await Client.GetStringAsync("/totals?year=2026&month=10", Token);

        Assert.Contains("October 2026", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "class=\"category-row").Count);
        Assert.Matches("<strong[^>]*>groceries</strong>", html);
        Assert.Matches("<strong[^>]*>dining</strong>", html);
        Assert.DoesNotMatch("<strong[^>]*>transport</strong>", html);
        Assert.Contains("30.30", html, StringComparison.Ordinal);
        Assert.Contains("35.30", html, StringComparison.Ordinal);
        Assert.DoesNotContain("$", html.Split("<main")[^1], StringComparison.Ordinal);
        Assert.Contains("api/reports/receipts.csv?year=2026&amp;month=10", html, StringComparison.Ordinal);
        Assert.Contains("href=\"api/reports/receipts.csv\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_category_links_to_the_approved_receipts_behind_it_in_that_month()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 10.10m, ReceiptCategory.Groceries);

        var html = await Client.GetStringAsync("/totals?year=2026&month=10", Token);

        Assert.Contains("href=\"receipts?status=approved&amp;category=groceries&amp;month=2026-10\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_empty_categories_are_hidden_until_asked_for()
    {
        await ApprovedAsync(new DateOnly(2026, 10, 2), 10.10m, ReceiptCategory.Groceries);

        var hidden = await Client.GetStringAsync("/totals?year=2026&month=10", Token);
        var all = await Client.GetStringAsync("/totals?year=2026&month=10&empty=1", Token);

        Assert.Single(Regex.Matches(hidden, "class=\"category-row"));
        Assert.Contains("7 categories have nothing in October 2026 and are hidden.", hidden, StringComparison.Ordinal);
        Assert.Equal(8, Regex.Matches(all, "class=\"category-row").Count);
        Assert.Equal(7, Regex.Matches(all, "category-row none").Count);
    }

    [Fact]
    public async Task An_empty_month_says_nothing_was_approved_and_has_a_zero_total()
    {
        var html = await Client.GetStringAsync("/totals?year=2026&month=10", Token);

        Assert.Contains("Nothing approved in October 2026 yet.", html, StringComparison.Ordinal);
        Assert.Contains(">0.00<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"category-row", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_totals_page_shows_the_change_on_the_month_before_and_the_twelve_month_chart()
    {
        await ApprovedAsync(new DateOnly(2026, 9, 10), 200.00m, ReceiptCategory.Groceries);
        await ApprovedAsync(new DateOnly(2026, 10, 10), 250.00m, ReceiptCategory.Groceries);

        var html = await Client.GetStringAsync("/totals?year=2026&month=10", Token);

        Assert.Contains("25% on September", html, StringComparison.Ordinal);
        Assert.Equal(12, Regex.Matches(html, "class=\"column").Count);
        Assert.Contains("Average a month", html, StringComparison.Ordinal);
        Assert.Contains("225.00", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_or_impossible_month_shows_the_current_one()
    {
        var current = DateTime.Now.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        Assert.Contains(current, await Client.GetStringAsync("/totals", Token), StringComparison.Ordinal);
        Assert.Contains(current, await Client.GetStringAsync("/totals?year=2026&month=13", Token), StringComparison.Ordinal);
        Assert.Contains(current, await Client.GetStringAsync("/totals?year=abc&month=4", Token), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/totals?year=abc&month=4")]
    [InlineData("/totals?year=2026&month=")]
    [InlineData("/totals?year=99999999999&month=1")]
    [InlineData("/totals?year=-5&month=-1")]
    [InlineData("/receipts?page=abc")]
    [InlineData("/receipts?page=99999999999")]
    [InlineData("/receipts?page=-3")]
    [InlineData("/receipts?page=")]
    public async Task Garbage_in_the_query_string_never_gives_an_error_page(string url)
    {
        var response = await Client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Previous_and_next_stay_between_the_first_approved_month_and_the_current_month()
    {
        await ApprovedAsync(new DateOnly(2020, 1, 15), 1m, ReceiptCategory.Other);
        await ApprovedAsync(new DateOnly(2020, 3, 15), 1m, ReceiptCategory.Other);

        var first = await Client.GetStringAsync("/totals?year=2020&month=1", Token);
        var middle = await Client.GetStringAsync("/totals?year=2020&month=2", Token);
        var now = DateTime.Now;
        var current = await Client.GetStringAsync($"/totals?year={now.Year}&month={now.Month}", Token);

        Assert.DoesNotContain("rel=\"prev\"", first, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", first, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", middle, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", middle, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", current, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", current, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_approved_receipts_previous_and_next_are_both_disabled()
    {
        var html = await Client.GetStringAsync("/totals", Token);

        Assert.DoesNotContain("rel=\"prev\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_thousand_approved_receipts_the_totals_page_loads_quickly_and_the_export_completes()
    {
        await SeedApprovedAsync(1000);
        var client = Client;
        await client.GetStringAsync("/totals?year=2026&month=10", Token);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var html = await client.GetStringAsync("/totals?year=2026&month=10", Token);
        var pageTook = timer.Elapsed;
        timer.Restart();
        var csv = await client.GetByteArrayAsync("/api/reports/receipts.csv", Token);
        var exportTook = timer.Elapsed;

        Assert.Contains("1000", html, StringComparison.Ordinal);
        Assert.True(pageTook < TimeSpan.FromSeconds(2), $"The totals page took {pageTook}.");
        Assert.True(exportTook < TimeSpan.FromSeconds(10), $"The export took {exportTook}.");
        Assert.Equal(1001, Encoding.UTF8.GetString(csv, 3, csv.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static readonly ReceiptCategory[] Categories = Enum.GetValues<ReceiptCategory>();
    private static readonly decimal[] SeedAmounts = [0.01m, 0.10m, 19.99m];

    private static List<(ReceiptCategory Category, decimal Total)> ExpectedSeed(int count) =>
        [.. Enumerable.Range(1, count).Select(g => (Categories[g % 8], SeedAmounts[g % 3]))];

    // Inserted straight into the table, since a thousand approvals through the handlers would take far longer.
    // The amounts are the kind that go wrong in binary floating point: 0.01, 0.10 and 19.99.
    private Task SeedApprovedAsync(int count) =>
        Fixture.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Receipts" ("Id","Status","OriginalFileName","ContentType","FileSizeBytes","Checksum","ImagePath",
                    "CreatedAt","UpdatedAt","Merchant","ReceiptDate","Total","Category","ApprovedAt")
                SELECT gen_random_uuid(), 'Approved', 'f' || g || '.png', 'image/png', 10, md5(g::text), 'p' || g || '.png',
                    now(), now(), 'Merchant ' || g, DATE '2026-10-01' + (g % 28),
                    (ARRAY[0.01, 0.10, 19.99])[1 + (g % 3)],
                    (ARRAY['Groceries','Dining','Transport','Utilities','Shopping','Health','Entertainment','Other'])[1 + (g % 8)],
                    now()
                FROM generate_series(1, {count}) AS g
                """, Token);
            return 0;
        });
}
