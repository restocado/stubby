using System.Net;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>The inbox, the add page and the filtered list, as the real app serves them over HTTP.</summary>
public class PagesHttpTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private HttpClient Client => Fixture.CreateWebClient();

    private async Task<string> GetPageAsync(string url)
    {
        var response = await Client.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Token);
    }

    private async Task<Guid> ReadAsync(string merchant = "Fake Cafe")
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Merchant = merchant };
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        return id;
    }

    private Task ApproveAsync(Guid id, string merchant, DateOnly date, decimal total, ReceiptCategory category) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, merchant, date, total, category), Token));

    [Fact]
    public async Task The_home_page_is_the_inbox_with_the_receipts_to_review_oldest_first()
    {
        var first = await ReadAsync("Older Cafe");
        var second = await ReadAsync("Newer Cafe");

        var html = await GetPageAsync("/");

        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("Inbox", html, StringComparison.Ordinal);
        Assert.Contains("2 receipts are waiting for you.", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Older Cafe", StringComparison.Ordinal) < html.IndexOf("Newer Cafe", StringComparison.Ordinal));
        Assert.Contains($"href=\"receipts/{first}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"receipts/{second}\"", html, StringComparison.Ordinal);
        Assert.Contains("Start reviewing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_inbox_starts_the_review_at_the_oldest_receipt()
    {
        var oldest = await ReadAsync("Older Cafe");
        await ReadAsync("Newer Cafe");

        var html = await GetPageAsync("/");

        var start = html.IndexOf("Start reviewing", StringComparison.Ordinal);
        Assert.Contains($"href=\"receipts/{oldest}\"", html[..start].Split("<a ")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_inbox_shows_a_failed_receipt_with_both_ways_forward()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());

        var html = await GetPageAsync("/");

        Assert.Contains("Couldn't be read", html, StringComparison.Ordinal);
        Assert.Contains("Try again", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"receipts/{id}?type=1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_to_do_the_inbox_says_so_and_offers_to_add_receipts()
    {
        var html = await GetPageAsync("/");

        Assert.Contains("all caught up", html, StringComparison.Ordinal);
        Assert.Contains("href=\"upload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_menu_counts_the_receipts_waiting_for_review()
    {
        await ReadAsync();
        await ReadAsync("Second Cafe");

        var html = await GetPageAsync("/receipts");

        Assert.Matches("class=\"count\"[^>]*>2</span>", html);
    }

    [Fact]
    public async Task The_inbox_names_the_receipt_that_was_just_approved()
    {
        var id = await ReadAsync();
        await ApproveAsync(id, "Final Cafe", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Dining);

        var html = await GetPageAsync($"/?approved={id}");

        Assert.Contains("Final Cafe", html, StringComparison.Ordinal);
        Assert.Contains("as dining", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?approved=not-a-guid")]
    [InlineData("/?approved=00000000-0000-0000-0000-000000000001")]
    public async Task A_made_up_approved_id_shows_no_message_and_no_error(string url)
    {
        var html = await GetPageAsync(url);

        Assert.DoesNotContain("class=\"toast\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_that_is_not_approved_is_never_announced_as_approved()
    {
        var id = await ReadAsync();

        var html = await GetPageAsync($"/?approved={id}");

        Assert.DoesNotContain("class=\"toast\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_add_page_has_a_drop_zone_and_a_camera_button()
    {
        var html = await GetPageAsync("/upload");

        Assert.Contains("Drop receipts here", html, StringComparison.Ordinal);
        Assert.Contains("capture=\"environment\"", html, StringComparison.Ordinal);
        Assert.Contains("accept=\"image/jpeg,image/png,application/pdf\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_search_finds_a_merchant_and_says_when_nothing_matches()
    {
        await ReadAsync("Corner Market");
        await ReadAsync("Hardware Hut");

        var found = await GetPageAsync("/receipts?search=market");
        var none = await GetPageAsync("/receipts?search=zebra");

        Assert.Contains("Corner Market", found, StringComparison.Ordinal);
        Assert.DoesNotContain("Hardware Hut", found, StringComparison.Ordinal);
        Assert.Contains("1 receipt match", found, StringComparison.Ordinal);
        Assert.Contains("No receipts match", none, StringComparison.Ordinal);
        Assert.Contains("Clear filters", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_status_filter_narrows_the_list_and_marks_the_chosen_tab()
    {
        await ReadAsync("Still Waiting Cafe");
        var approved = await ReadAsync("Approved Cafe");
        await ApproveAsync(approved, "Approved Cafe", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Dining);

        var html = await GetPageAsync("/receipts?status=approved");

        Assert.Contains("Approved Cafe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Still Waiting Cafe", html, StringComparison.Ordinal);
        Assert.Matches("class=\"on\"[^>]*>Approved</a>", html);
    }

    [Fact]
    public async Task The_category_and_month_filters_come_from_the_totals_links()
    {
        var groceries = await ReadAsync("Market A");
        await ApproveAsync(groceries, "Market A", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Groceries);
        var dining = await ReadAsync("Cafe B");
        await ApproveAsync(dining, "Cafe B", new DateOnly(2026, 10, 4), 9.00m, ReceiptCategory.Dining);
        var other = await ReadAsync("Market C");
        await ApproveAsync(other, "Market C", new DateOnly(2026, 9, 4), 7.00m, ReceiptCategory.Groceries);

        var html = await GetPageAsync("/receipts?status=approved&category=groceries&month=2026-10");

        Assert.Contains("Market A", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Cafe B", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Market C", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/receipts?status=zzz")]
    [InlineData("/receipts?month=abc")]
    [InlineData("/receipts?month=2026-13")]
    [InlineData("/receipts?month=0000-01")]
    [InlineData("/receipts?category=nonsense")]
    [InlineData("/receipts?category=99")]
    [InlineData("/receipts?search=%25_%5C")]
    [InlineData("/totals?empty=maybe")]
    [InlineData("/receipts/00000000-0000-0000-0000-000000000001?type=2&approved=x")]
    public async Task Garbage_in_the_new_query_parts_never_gives_an_error_page(string url)
    {
        var response = await Client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_very_long_search_is_cut_and_does_not_fail()
    {
        var response = await Client.GetAsync("/receipts?search=" + new string('a', 500), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
