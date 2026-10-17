using Stubby.Application.Features.Receipts.CountReceiptsToReview;
using Stubby.Application.Features.Receipts.GetInbox;
using Stubby.Application.Features.Receipts.GetReviewQueue;
using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Tests.Receipts;

public class InboxAndReviewQueueTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReceiptRepository _receipts = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_inbox_lists_receipts_to_review_oldest_first_with_what_was_read()
    {
        var newest = Add(ForReview(5, "Newest Cafe", flagReasons: "TotalAtOrAboveLimit, DateMissing"));
        var oldest = Add(ForReview(1, "Oldest Cafe"));

        var inbox = await Inbox();

        Assert.Equal([oldest.Id, newest.Id], inbox.ForReview.Select(i => i.Id));
        Assert.Equal(2, inbox.ForReviewCount);
        var first = inbox.ForReview[0];
        Assert.Equal("Oldest Cafe", first.Merchant);
        Assert.Equal(12.50m, first.Total);
        Assert.Empty(first.FlagReasons);
        Assert.Equal([FlagReason.TotalAtOrAboveLimit, FlagReason.DateMissing], inbox.ForReview[1].FlagReasons);
    }

    [Fact]
    public async Task The_inbox_lists_failed_receipts_newest_first_with_the_reason()
    {
        var older = Add(Failed(1, "The provider returned HTTP 503."));
        var newer = Add(Failed(4, "The provider timed out."));

        var inbox = await Inbox();

        Assert.Equal([newer.Id, older.Id], inbox.Failed.Select(i => i.Id));
        Assert.Equal("The provider timed out.", inbox.Failed[0].Error);
        Assert.Null(inbox.Failed[0].Merchant);
        Assert.Equal(2, inbox.FailedCount);
    }

    [Fact]
    public async Task Queued_and_processing_receipts_are_counted_as_being_read_and_not_listed()
    {
        Add(Queued(1));
        var processing = Add(Queued(2));
        processing.StartProcessing(Start);
        Add(ForReview(3, "Done Cafe"));

        var inbox = await Inbox();

        Assert.Equal(2, inbox.ReadingCount);
        Assert.Single(inbox.ForReview);
        Assert.Empty(inbox.Failed);
    }

    [Fact]
    public async Task Approved_receipts_are_not_in_the_inbox()
    {
        var receipt = Add(ForReview(1, "Approved Cafe"));
        receipt.Approve("Approved Cafe", new DateOnly(2026, 9, 1), 12.50m, ReceiptCategory.Dining, Start);

        var inbox = await Inbox();

        Assert.Empty(inbox.ForReview);
        Assert.Equal(0, inbox.ForReviewCount);
    }

    [Fact]
    public async Task A_long_list_is_cut_to_the_limit_but_the_count_is_the_whole_number()
    {
        for (var i = 0; i < GetInboxQuery.Limit + 10; i++)
        {
            Add(ForReview(i, $"Cafe {i}"));
        }

        var inbox = await Inbox();

        Assert.Equal(GetInboxQuery.Limit, inbox.ForReview.Count);
        Assert.Equal(GetInboxQuery.Limit + 10, inbox.ForReviewCount);
    }

    [Fact]
    public async Task A_pdf_is_marked_as_one()
    {
        var pdf = Receipt.Create("bill.pdf", "application/pdf", 100, "pdf-checksum", "pdf-path", Start);
        pdf.StartProcessing(Start);
        pdf.CompleteExtraction("Pdf Cafe", null, 5m, "m", "v1", false, null, Start);
        Add(pdf);

        var item = Assert.Single((await Inbox()).ForReview);

        Assert.True(item.IsPdf);
    }

    [Fact]
    public async Task The_review_queue_gives_the_place_and_the_next_receipt()
    {
        var ids = new[] { Add(ForReview(1, "A")).Id, Add(ForReview(2, "B")).Id, Add(ForReview(3, "C")).Id };

        var queue = await Queue(ids[1]);

        Assert.Equal(2, queue.Position);
        Assert.Equal(3, queue.Count);
        Assert.Equal(ids[2], queue.NextId);
    }

    [Fact]
    public async Task After_the_newest_comes_the_oldest_again()
    {
        var oldest = Add(ForReview(1, "A"));
        Add(ForReview(2, "B"));
        var newest = Add(ForReview(3, "C"));

        var queue = await Queue(newest.Id);

        Assert.Equal(3, queue.Position);
        Assert.Equal(oldest.Id, queue.NextId);
    }

    [Fact]
    public async Task When_the_current_receipt_is_the_only_one_there_is_no_next()
    {
        var only = Add(ForReview(1, "A"));

        var queue = await Queue(only.Id);

        Assert.Equal(1, queue.Position);
        Assert.Equal(1, queue.Count);
        Assert.Null(queue.NextId);
    }

    [Fact]
    public async Task A_receipt_that_is_not_waiting_has_no_place_and_the_next_is_the_oldest_waiting()
    {
        var failed = Add(Failed(1, "error"));
        var oldest = Add(ForReview(2, "A"));
        Add(ForReview(3, "B"));

        var queue = await Queue(failed.Id);

        Assert.Null(queue.Position);
        Assert.Equal(2, queue.Count);
        Assert.Equal(oldest.Id, queue.NextId);
    }

    [Fact]
    public async Task With_nothing_waiting_there_is_no_next()
    {
        var queue = await Queue(Guid.NewGuid());

        Assert.Null(queue.Position);
        Assert.Equal(0, queue.Count);
        Assert.Null(queue.NextId);
    }

    [Fact]
    public async Task The_count_for_the_menu_is_the_receipts_waiting_for_review()
    {
        Add(ForReview(1, "A"));
        Add(ForReview(2, "B"));
        Add(Failed(3, "error"));
        Add(Queued(4));

        var count = await new CountReceiptsToReviewQueryHandler(_receipts).Handle(new CountReceiptsToReviewQuery(), Token);

        Assert.Equal(2, count);
    }

    private Receipt Add(Receipt receipt)
    {
        _receipts.Receipts.Add(receipt);
        return receipt;
    }

    private static Receipt Queued(int n) =>
        Receipt.Create($"receipt-{n}.png", "image/png", 100, $"checksum-{n}", $"path-{n}", Start.AddMinutes(n));

    private static Receipt ForReview(int n, string merchant, string? flagReasons = null)
    {
        var receipt = Queued(n);
        receipt.StartProcessing(Start);
        receipt.CompleteExtraction(
            merchant, new DateOnly(2026, 9, 3), 12.50m, "test-model", "v1", flagReasons is not null, flagReasons, Start);
        return receipt;
    }

    private static Receipt Failed(int n, string error)
    {
        var receipt = Queued(n);
        receipt.StartProcessing(Start);
        receipt.FailAttempt(error, 1, Start);
        return receipt;
    }

    private async Task<GetInboxResponse> Inbox() =>
        await new GetInboxQueryHandler(_receipts).Handle(new GetInboxQuery(), Token);

    private async Task<GetReviewQueueResponse> Queue(Guid current) =>
        await new GetReviewQueueQueryHandler(_receipts).Handle(new GetReviewQueueQuery(current), Token);
}
