using FluentValidation;
using Stubby.Application.Features.Receipts.ListReceipts;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

public class ListReceiptsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ExtractedDate = new(2026, 9, 3);
    private static readonly DateOnly FinalDate = new(2026, 9, 4);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly ListReceiptsQueryHandler _handler;

    public ListReceiptsTests()
    {
        _handler = new ListReceiptsQueryHandler(new ListReceiptsQueryValidator(), _receipts);
    }

    [Fact]
    public async Task Sixty_receipts_make_three_pages_of_25_25_and_10()
    {
        await AddQueued(60);

        var first = await List(1);
        var second = await List(2);
        var third = await List(3);

        Assert.Equal([25, 25, 10], [first.Items.Count, second.Items.Count, third.Items.Count]);
        Assert.All([first, second, third], page =>
        {
            Assert.Equal(60, page.TotalCount);
            Assert.Equal(3, page.PageCount);
        });
    }

    [Fact]
    public async Task Newest_receipt_comes_first()
    {
        var items = await AddQueued(5);

        var response = await List(1);

        Assert.Equal(items.OrderByDescending(r => r.CreatedAt).Select(r => r.Id), response.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Receipts_with_the_same_created_time_are_ordered_by_id_descending()
    {
        for (var i = 0; i < 10; i++)
        {
            await Add(Receipt.Create($"r{i}.png", "image/png", 10, $"checksum-{i}", $"path-{i}", Start));
        }

        var response = await List(1);

        Assert.Equal(_receipts.Receipts.Select(r => r.Id).OrderByDescending(id => id), response.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Page_past_the_end_is_empty()
    {
        await AddQueued(3);

        var response = await List(2);

        Assert.Empty(response.Items);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(1, response.PageCount);
    }

    [Fact]
    public async Task No_receipts_gives_an_empty_list_with_no_pages()
    {
        var response = await List(1);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(0, response.PageCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Page_below_one_fails_validation(int page)
    {
        await Assert.ThrowsAsync<ValidationException>(async () => await List(page));
    }

    [Fact]
    public void Page_defaults_to_one()
    {
        Assert.Equal(1, new ListReceiptsQuery().Page);
    }

    [Fact]
    public async Task Queued_receipt_has_no_values_and_cannot_be_opened_or_deleted()
    {
        await Add(Queued(0));

        var item = Assert.Single((await List(1)).Items);

        Assert.Equal(ReceiptStatus.Queued, item.Status);
        Assert.Null(item.Merchant);
        Assert.Null(item.ReceiptDate);
        Assert.Null(item.Total);
        Assert.Null(item.Category);
        Assert.False(item.CanOpen);
        Assert.False(item.CanDelete);
    }

    [Fact]
    public async Task Processing_receipt_cannot_be_opened_or_deleted()
    {
        var receipt = Queued(0);
        receipt.StartProcessing(Start);
        await Add(receipt);

        var item = Assert.Single((await List(1)).Items);

        Assert.Equal(ReceiptStatus.Processing, item.Status);
        Assert.False(item.CanOpen);
        Assert.False(item.CanDelete);
    }

    [Fact]
    public async Task ForReview_receipt_shows_extracted_values_and_its_flag()
    {
        await Add(ForReview(0, isFlagged: true));

        var item = Assert.Single((await List(1)).Items);

        Assert.Equal(ReceiptStatus.ForReview, item.Status);
        Assert.True(item.IsFlagged);
        Assert.Equal("Extracted Cafe", item.Merchant);
        Assert.Equal(ExtractedDate, item.ReceiptDate);
        Assert.Equal(12.50m, item.Total);
        Assert.Null(item.Category);
        Assert.True(item.CanOpen);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public async Task Failed_receipt_has_no_values_but_can_be_opened_and_deleted()
    {
        var receipt = Queued(0);
        receipt.StartProcessing(Start);
        receipt.FailAttempt("provider down", 1, Start);
        await Add(receipt);

        var item = Assert.Single((await List(1)).Items);

        Assert.Equal(ReceiptStatus.Failed, item.Status);
        Assert.Null(item.Merchant);
        Assert.Null(item.Total);
        Assert.True(item.CanOpen);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public async Task Approved_receipt_shows_final_values_and_category()
    {
        var receipt = ForReview(0, isFlagged: false);
        receipt.Approve("Final Cafe", FinalDate, 13.00m, ReceiptCategory.Dining, Start);
        await Add(receipt);

        var item = Assert.Single((await List(1)).Items);

        Assert.Equal(ReceiptStatus.Approved, item.Status);
        Assert.Equal("Final Cafe", item.Merchant);
        Assert.Equal(FinalDate, item.ReceiptDate);
        Assert.Equal(13.00m, item.Total);
        Assert.Equal(ReceiptCategory.Dining, item.Category);
        Assert.False(item.CanOpen);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public async Task Every_item_carries_its_transaction_number_and_file_name()
    {
        await AddQueued(3);

        var response = await List(1);

        Assert.Equal([3L, 2L, 1L], response.Items.Select(i => i.TransactionNo));
        Assert.All(response.Items, i => Assert.EndsWith(".png", i.OriginalFileName, StringComparison.Ordinal));
    }

    private async Task<List<Receipt>> AddQueued(int count)
    {
        var added = new List<Receipt>();
        for (var i = 0; i < count; i++)
        {
            var receipt = Queued(i);
            await Add(receipt);
            added.Add(receipt);
        }

        return added;
    }

    private static Receipt Queued(int n) =>
        Receipt.Create($"receipt-{n}.png", "image/png", 100, $"checksum-{n}", $"path-{n}", Start.AddMinutes(n));

    private static Receipt ForReview(int n, bool isFlagged)
    {
        var receipt = Queued(n);
        receipt.StartProcessing(Start);
        receipt.CompleteExtraction(
            "Extracted Cafe", ExtractedDate, 12.50m, "test-model", "v1", isFlagged, isFlagged ? "OverLimit" : null, Start);
        return receipt;
    }

    private async Task Add(Receipt receipt) =>
        Assert.True(await _receipts.TryAddAsync(receipt, TestContext.Current.CancellationToken));

    private async Task<ListReceiptsResponse> List(int page) =>
        await _handler.Handle(new ListReceiptsQuery(page), TestContext.Current.CancellationToken);
}
