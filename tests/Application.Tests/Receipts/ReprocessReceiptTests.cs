using Stubby.Application.Features.Receipts.ReprocessAllFailedReceipts;
using Stubby.Application.Features.Receipts.ReprocessReceipt;
using Stubby.Application.Tests.Extraction;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

public class ReprocessReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly ReprocessReceiptCommandHandler _one;
    private readonly ReprocessAllFailedReceiptsCommandHandler _all;

    public ReprocessReceiptTests()
    {
        var clock = new FixedTimeProvider(Now.AddHours(1));
        _one = new ReprocessReceiptCommandHandler(_receipts, clock);
        _all = new ReprocessAllFailedReceiptsCommandHandler(_receipts, clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Failed_receipt_with_three_attempts_is_Queued_again_with_none_and_no_error()
    {
        var receipt = await AddAsync(ReceiptStatus.Failed, attempts: 3);
        var createdAt = receipt.CreatedAt;

        var response = await _one.Handle(new ReprocessReceiptCommand(receipt.Id), Token);

        Assert.IsType<ReprocessReceiptResponse.Requeued>(response);
        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
        Assert.Equal(0, receipt.AttemptsUsed);
        Assert.Null(receipt.LastError);
        Assert.Equal(createdAt, receipt.CreatedAt);
        Assert.Equal(1, receipt.TransactionNo);
    }

    [Theory]
    [InlineData(ReceiptStatus.Queued)]
    [InlineData(ReceiptStatus.Processing)]
    [InlineData(ReceiptStatus.ForReview)]
    [InlineData(ReceiptStatus.Approved)]
    public async Task A_receipt_that_is_not_Failed_returns_conflict_and_changes_nothing(ReceiptStatus status)
    {
        var receipt = await AddAsync(status);

        var response = await _one.Handle(new ReprocessReceiptCommand(receipt.Id), Token);

        Assert.IsType<ReprocessReceiptResponse.Conflict>(response);
        Assert.Equal(status, receipt.Status);
    }

    [Fact]
    public async Task An_unknown_id_returns_not_found()
    {
        Assert.IsType<ReprocessReceiptResponse.NotFound>(await _one.Handle(new ReprocessReceiptCommand(Guid.NewGuid()), Token));
    }

    [Fact]
    public async Task All_requeues_every_Failed_receipt_and_leaves_the_others_alone()
    {
        var failed = new List<Receipt>();
        for (var i = 0; i < 5; i++)
        {
            failed.Add(await AddAsync(ReceiptStatus.Failed, attempts: 1 + i));
        }

        var forReview = new[] { await AddAsync(ReceiptStatus.ForReview), await AddAsync(ReceiptStatus.ForReview) };
        var approved = await AddAsync(ReceiptStatus.Approved);

        var response = await _all.Handle(new ReprocessAllFailedReceiptsCommand(), Token);

        Assert.Equal(5, response.RequeuedCount);
        Assert.All(failed, r =>
        {
            Assert.Equal(ReceiptStatus.Queued, r.Status);
            Assert.Equal(0, r.AttemptsUsed);
            Assert.Null(r.LastError);
        });
        Assert.All(forReview, r => Assert.Equal(ReceiptStatus.ForReview, r.Status));
        Assert.Equal(ReceiptStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task All_with_no_Failed_receipts_returns_zero()
    {
        await AddAsync(ReceiptStatus.ForReview);

        var response = await _all.Handle(new ReprocessAllFailedReceiptsCommand(), Token);

        Assert.Equal(0, response.RequeuedCount);
    }

    private async Task<Receipt> AddAsync(ReceiptStatus status, int attempts = 1)
    {
        var n = _receipts.Receipts.Count;
        var receipt = Receipt.Create($"r{n}.png", "image/png", 10, $"checksum-{n}", $"path-{n}", Now.AddMinutes(-n));
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        switch (status)
        {
            case ReceiptStatus.Processing:
                receipt.StartProcessing(Now);
                break;
            case ReceiptStatus.Failed:
                for (var i = 0; i < attempts; i++)
                {
                    receipt.StartProcessing(Now);
                    receipt.FailAttempt("The provider returned HTTP 503.", attempts, Now);
                }

                break;
            case ReceiptStatus.ForReview or ReceiptStatus.Approved:
                receipt.StartProcessing(Now);
                receipt.CompleteExtraction("Cafe", new DateOnly(2026, 10, 2), 5m, "m", "v1", false, null, Now);
                if (status == ReceiptStatus.Approved)
                {
                    receipt.Approve("Cafe", new DateOnly(2026, 10, 2), 5m, ReceiptCategory.Dining, Now);
                }

                break;
        }

        return receipt;
    }
}
