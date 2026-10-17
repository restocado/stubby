using Microsoft.Extensions.Logging.Abstractions;
using Stubby.Application.Features.Receipts.GetReceiptForReview;
using Stubby.Application.Features.Receipts.GetReceiptImage;
using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Tests.Receipts;

public class ReviewReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReceiptDate = new(2026, 10, 2);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly FakeImageStore _images = new();
    private readonly GetReceiptForReviewQueryHandler _review;
    private readonly GetReceiptImageQueryHandler _image;

    public ReviewReceiptTests()
    {
        _review = new GetReceiptForReviewQueryHandler(_receipts);
        _image = new GetReceiptImageQueryHandler(_receipts, _images, NullLogger<GetReceiptImageQueryHandler>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ReceiptForReview> OpenAsync(Receipt receipt) =>
        Assert.IsType<GetReceiptForReviewResponse.Found>(
            await _review.Handle(new GetReceiptForReviewQuery(receipt.Id), Token)).Receipt;

    [Fact]
    public async Task ForReview_returns_the_extracted_fields_the_flag_and_its_reasons()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.CompleteExtraction("Fake Cafe", null, 5000m, "m", "v1", isFlagged: true, "TotalAtOrAboveLimit,DateMissing", Now);
        });

        var opened = await OpenAsync(receipt);

        Assert.Equal(receipt.TransactionNo, opened.TransactionNo);
        Assert.Equal(ReceiptStatus.ForReview, opened.Status);
        Assert.Equal("receipt.png", opened.OriginalFileName);
        Assert.Equal("image/png", opened.ContentType);
        Assert.Equal("Fake Cafe", opened.ExtractedMerchant);
        Assert.Null(opened.ExtractedDate);
        Assert.Equal(5000m, opened.ExtractedTotal);
        Assert.True(opened.IsFlagged);
        Assert.Equal([FlagReason.TotalAtOrAboveLimit, FlagReason.DateMissing], opened.FlagReasons);
        Assert.Null(opened.Merchant);
        Assert.Null(opened.LastError);
    }

    [Fact]
    public async Task A_flag_reason_this_version_does_not_know_is_skipped()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.CompleteExtraction("Cafe", ReceiptDate, 5m, "m", "v1", isFlagged: true, "DateMissing,SomethingNew", Now);
        });

        Assert.Equal([FlagReason.DateMissing], (await OpenAsync(receipt)).FlagReasons);
    }

    [Fact]
    public async Task An_unflagged_receipt_has_no_reasons()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.CompleteExtraction("Cafe", ReceiptDate, 5m, "m", "v1", isFlagged: false, null, Now);
        });

        var opened = await OpenAsync(receipt);

        Assert.False(opened.IsFlagged);
        Assert.Empty(opened.FlagReasons);
    }

    [Fact]
    public async Task Failed_returns_no_extracted_fields_and_the_last_error()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.FailAttempt("The provider returned HTTP 503.", 1, Now);
        });

        var opened = await OpenAsync(receipt);

        Assert.Equal(ReceiptStatus.Failed, opened.Status);
        Assert.Null(opened.ExtractedMerchant);
        Assert.Null(opened.ExtractedDate);
        Assert.Null(opened.ExtractedTotal);
        Assert.Equal("The provider returned HTTP 503.", opened.LastError);
    }

    [Fact]
    public async Task Approved_returns_the_final_values_and_the_category()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.CompleteExtraction("Fake Cafe", ReceiptDate, 12.50m, "m", "v1", false, null, Now);
            r.Approve("Final Cafe", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Dining, Now);
        });

        var opened = await OpenAsync(receipt);

        Assert.Equal(ReceiptStatus.Approved, opened.Status);
        Assert.Equal("Final Cafe", opened.Merchant);
        Assert.Equal(new DateOnly(2026, 10, 3), opened.ReceiptDate);
        Assert.Equal(13.00m, opened.Total);
        Assert.Equal(ReceiptCategory.Dining, opened.Category);
        Assert.Equal(12.50m, opened.ExtractedTotal);
    }

    [Fact]
    public async Task Queued_and_Processing_receipts_are_locked_for_both_the_query_and_the_image()
    {
        var queued = await AddAsync(_ => { });
        var processing = await AddAsync(r => r.StartProcessing(Now));

        foreach (var receipt in new[] { queued, processing })
        {
            Assert.IsType<GetReceiptForReviewResponse.Locked>(await _review.Handle(new GetReceiptForReviewQuery(receipt.Id), Token));
            Assert.IsType<GetReceiptImageResponse.Locked>(await _image.Handle(new GetReceiptImageQuery(receipt.Id), Token));
        }
    }

    [Fact]
    public async Task An_unknown_id_is_not_found_for_both_the_query_and_the_image()
    {
        Assert.IsType<GetReceiptForReviewResponse.NotFound>(await _review.Handle(new GetReceiptForReviewQuery(Guid.NewGuid()), Token));
        Assert.IsType<GetReceiptImageResponse.NotFound>(await _image.Handle(new GetReceiptImageQuery(Guid.NewGuid()), Token));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public async Task The_image_is_the_stored_file_with_its_content_type(string contentType)
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6];
        var receipt = await AddAsync(r => r.StartProcessing(Now), contentType, bytes);
        receipt.CompleteExtraction("Cafe", ReceiptDate, 5m, "m", "v1", false, null, Now);

        var response = await _image.Handle(new GetReceiptImageQuery(receipt.Id), Token);

        var found = Assert.IsType<GetReceiptImageResponse.Found>(response);
        Assert.Equal(bytes, found.Content);
        Assert.Equal(contentType, found.ContentType);
    }

    [Fact]
    public async Task A_missing_file_is_not_found()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.FailAttempt("failed", 1, Now);
        });
        _images.Files.Clear();

        Assert.IsType<GetReceiptImageResponse.NotFound>(await _image.Handle(new GetReceiptImageQuery(receipt.Id), Token));
    }

    [Fact]
    public async Task A_stored_path_that_the_store_refuses_is_never_served()
    {
        var receipt = await AddAsync(r =>
        {
            r.StartProcessing(Now);
            r.FailAttempt("failed", 1, Now);
        });
        var handler = new GetReceiptImageQueryHandler(
            _receipts, new RefusingImageStore(), NullLogger<GetReceiptImageQueryHandler>.Instance);

        Assert.IsType<GetReceiptImageResponse.NotFound>(await handler.Handle(new GetReceiptImageQuery(receipt.Id), Token));
    }

    [Fact]
    public void The_query_takes_an_id_and_no_path()
    {
        var parameter = Assert.Single(typeof(GetReceiptImageQuery).GetConstructors().Single().GetParameters());

        Assert.Equal(typeof(Guid), parameter.ParameterType);
    }

    private async Task<Receipt> AddAsync(Action<Receipt> configure, string contentType = "image/png", byte[]? bytes = null)
    {
        var n = _receipts.Receipts.Count;
        var content = bytes ?? [1, 2, 3, (byte)n];
        var path = await _images.SaveAsync(content, contentType, Token);
        var receipt = Receipt.Create("receipt.png", contentType, content.Length, $"checksum-{n}", path, Now.AddMinutes(-n));
        configure(receipt);
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        return receipt;
    }

    private sealed class RefusingImageStore : Stubby.Application.Abstractions.IImageStore
    {
        public Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromException<byte[]>(new InvalidOperationException("Path is outside the storage root."));

        public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
