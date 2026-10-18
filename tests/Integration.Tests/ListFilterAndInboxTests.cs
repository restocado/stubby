using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Application.Features.Receipts.CountReceiptsToReview;
using Stubby.Application.Features.Receipts.GetInbox;
using Stubby.Application.Features.Receipts.GetReviewQueue;
using Stubby.Application.Features.Receipts.ListReceipts;
using Stubby.Application.Features.Reports.GetTotalsTrend;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>The list filters, the inbox, the review queue and the monthly sums, through the real handlers and database.</summary>
public class ListFilterAndInboxTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private int _files;

    private Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(query, Token));

    private Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(command, Token));

    private async Task<IReadOnlyList<Guid>> MatchesAsync(ReceiptFilter filter)
    {
        var query = new ListReceiptsQuery(1, filter.Search, filter.Status, filter.Year, filter.Month, filter.Category);
        return (await SendAsync(query)).Items.Select(i => i.Id).ToList();
    }

    /// <summary>Uploads a receipt and has the worker step read it, so it ends ForReview with what the stub extractor returns.</summary>
    private async Task<Guid> ForReviewAsync(string merchant, DateOnly? date, decimal total = 12.50m, string? fileName = null)
    {
        Fixture.Extractor.Result = new ExtractionResult(merchant, date, total, null, null, "stub-model:1b", "v1");
        var n = Interlocked.Increment(ref _files);
        var id = await UploadAsync(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, (byte)(n >> 8), (byte)n, 7, 7],
            fileName ?? $"scan-{n}.png",
            "image/png");
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        return id;
    }

    private async Task<Guid> ApprovedAsync(
        string merchant, DateOnly date, decimal total, ReceiptCategory category = ReceiptCategory.Dining)
    {
        var id = await ForReviewAsync("Read Cafe", new DateOnly(2020, 1, 1));
        var response = await SendAsync(new ApproveReceiptCommand(id, merchant, date, total, category));
        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        return id;
    }

    private async Task<Guid> FailedAsync()
    {
        Fixture.Extractor.Failure = new InvalidOperationException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        await ProcessOneAsync();
        Fixture.Extractor.Failure = null;
        return id;
    }

    [Fact]
    public async Task Search_matches_part_of_a_merchant_ignoring_case()
    {
        var roma = await ForReviewAsync("Cafe Roma", new DateOnly(2026, 9, 1));
        await ForReviewAsync("Hardware Hut", new DateOnly(2026, 9, 1));

        Assert.Equal([roma], await MatchesAsync(new ReceiptFilter(Search: "ROMA")));
        Assert.Equal([roma], await MatchesAsync(new ReceiptFilter(Search: "fe rom")));
    }

    [Fact]
    public async Task Search_uses_the_final_merchant_of_an_approved_receipt_and_not_what_was_read()
    {
        var id = await ApprovedAsync("Corrected Name", new DateOnly(2026, 9, 1), 5m);

        Assert.Equal([id], await MatchesAsync(new ReceiptFilter(Search: "corrected")));
        Assert.Empty(await MatchesAsync(new ReceiptFilter(Search: "Read Cafe")));
    }

    [Fact]
    public async Task Search_also_matches_the_file_name_so_a_receipt_that_could_not_be_read_can_be_found()
    {
        var failed = await FailedAsync();
        await ForReviewAsync("Other Place", new DateOnly(2026, 9, 1), fileName: "holiday.png");

        Assert.Equal([failed], await MatchesAsync(new ReceiptFilter(Search: "receipt.png")));
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_ordinary_characters()
    {
        var percent = await ForReviewAsync("Half 50% Off Shop", new DateOnly(2026, 9, 1));
        var underscore = await ForReviewAsync("snake_case Store", new DateOnly(2026, 9, 1));
        await ForReviewAsync("Plain Shop", new DateOnly(2026, 9, 1));

        Assert.Equal([percent], await MatchesAsync(new ReceiptFilter(Search: "50%")));
        Assert.Equal([percent], await MatchesAsync(new ReceiptFilter(Search: "%")));
        Assert.Equal([underscore], await MatchesAsync(new ReceiptFilter(Search: "e_c")));
        Assert.Empty(await MatchesAsync(new ReceiptFilter(Search: @"\")));
    }

    [Fact]
    public async Task The_status_filter_picks_the_right_receipts_and_all_includes_the_ones_being_read()
    {
        var forReview = await ForReviewAsync("Cafe A", new DateOnly(2026, 9, 1));
        var approved = await ApprovedAsync("Cafe B", new DateOnly(2026, 9, 2), 5m);
        var failed = await FailedAsync();
        var queued = await UploadAsync();

        Assert.Equal([forReview], await MatchesAsync(new ReceiptFilter(Status: ReceiptStatusFilter.ToReview)));
        Assert.Equal([approved], await MatchesAsync(new ReceiptFilter(Status: ReceiptStatusFilter.Approved)));
        Assert.Equal([failed], await MatchesAsync(new ReceiptFilter(Status: ReceiptStatusFilter.Failed)));
        Assert.Equal(4, (await MatchesAsync(new ReceiptFilter())).Count);
        Assert.Contains(queued, await MatchesAsync(new ReceiptFilter()));
    }

    [Fact]
    public async Task The_month_filter_uses_the_receipts_date_with_the_edges_of_the_month_included()
    {
        var first = await ApprovedAsync("A", new DateOnly(2026, 9, 1), 1m);
        var last = await ApprovedAsync("B", new DateOnly(2026, 9, 30), 1m);
        await ApprovedAsync("C", new DateOnly(2026, 10, 1), 1m);
        await ApprovedAsync("D", new DateOnly(2026, 8, 31), 1m);
        var read = await ForReviewAsync("E", new DateOnly(2026, 9, 15));
        await ForReviewAsync("No date", null);

        var september = await MatchesAsync(new ReceiptFilter(Year: 2026, Month: 9));

        Assert.Equal(new[] { first, last, read }.Order(), september.Order());
    }

    [Fact]
    public async Task The_category_filter_matches_approved_receipts_of_that_category()
    {
        var groceries = await ApprovedAsync("A", new DateOnly(2026, 9, 1), 1m, ReceiptCategory.Groceries);
        await ApprovedAsync("B", new DateOnly(2026, 9, 1), 1m, ReceiptCategory.Dining);
        await ForReviewAsync("C", new DateOnly(2026, 9, 1));

        Assert.Equal([groceries], await MatchesAsync(new ReceiptFilter(Category: ReceiptCategory.Groceries)));
    }

    [Fact]
    public async Task The_filters_combine_and_the_count_is_of_the_matches_only()
    {
        var wanted = await ApprovedAsync("Corner Market", new DateOnly(2026, 9, 5), 9m, ReceiptCategory.Groceries);
        await ApprovedAsync("Corner Market", new DateOnly(2026, 10, 5), 9m, ReceiptCategory.Groceries);
        await ApprovedAsync("Corner Market", new DateOnly(2026, 9, 6), 9m, ReceiptCategory.Dining);
        await ApprovedAsync("Elsewhere", new DateOnly(2026, 9, 7), 9m, ReceiptCategory.Groceries);

        var response = await SendAsync(new ListReceiptsQuery(
            1, "market", ReceiptStatusFilter.Approved, 2026, 9, ReceiptCategory.Groceries));

        Assert.Equal([wanted], response.Items.Select(i => i.Id));
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task The_inbox_lists_what_needs_attention_and_counts_what_is_being_read()
    {
        var older = await ForReviewAsync("Older", new DateOnly(2026, 9, 1));
        var newer = await ForReviewAsync("Newer", new DateOnly(2026, 9, 2), total: 4000m);
        var failed = await FailedAsync();
        await ApprovedAsync("Done", new DateOnly(2026, 9, 3), 5m);
        await UploadAsync();

        var inbox = await SendAsync(new GetInboxQuery());

        Assert.Equal([older, newer], inbox.ForReview.Select(i => i.Id));
        Assert.Equal(2, inbox.ForReviewCount);
        Assert.NotEmpty(inbox.ForReview[1].FlagReasons);
        Assert.Equal([failed], inbox.Failed.Select(i => i.Id));
        Assert.False(string.IsNullOrWhiteSpace(inbox.Failed[0].Error));
        Assert.Equal(1, inbox.ReadingCount);
    }

    [Fact]
    public async Task The_review_queue_runs_oldest_first_and_goes_round()
    {
        var a = await ForReviewAsync("A", new DateOnly(2026, 9, 1));
        var b = await ForReviewAsync("B", new DateOnly(2026, 9, 1));
        var c = await ForReviewAsync("C", new DateOnly(2026, 9, 1));

        var fromA = await SendAsync(new GetReviewQueueQuery(a));
        var fromC = await SendAsync(new GetReviewQueueQuery(c));

        Assert.Equal((1, 3, b), (fromA.Position, fromA.Count, fromA.NextId));
        Assert.Equal((3, 3, a), (fromC.Position, fromC.Count, fromC.NextId));
        Assert.Equal(3, await SendAsync(new CountReceiptsToReviewQuery()));
    }

    [Fact]
    public async Task Monthly_sums_cover_the_window_and_only_approved_receipts()
    {
        await ApprovedAsync("A", new DateOnly(2025, 12, 31), 10.10m);
        await ApprovedAsync("B", new DateOnly(2026, 1, 1), 20.20m);
        await ApprovedAsync("C", new DateOnly(2026, 1, 31), 0.05m);
        await ApprovedAsync("D", new DateOnly(2026, 2, 1), 7m);
        await ApprovedAsync("Outside", new DateOnly(2024, 6, 1), 99m);
        await ForReviewAsync("Not approved", new DateOnly(2026, 1, 15), total: 500m);

        var trend = await SendAsync(new GetTotalsTrendQuery(2026, 2));

        Assert.Equal(12, trend.Months.Count);
        var totals = trend.Months.ToDictionary(m => m.Month, m => m.Total);
        Assert.Equal(10.10m, totals[new YearMonth(2025, 12)]);
        Assert.Equal(20.25m, totals[new YearMonth(2026, 1)]);
        Assert.Equal(7m, totals[new YearMonth(2026, 2)]);
        Assert.Equal(0m, totals[new YearMonth(2025, 11)]);
        Assert.Equal(20.25m, trend.PreviousMonthTotal);
        Assert.Equal(-65.4m, trend.ChangePercent);
        Assert.Equal(3, trend.MonthsWithReceipts);
    }
}
