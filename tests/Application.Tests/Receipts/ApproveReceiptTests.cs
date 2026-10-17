using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Application.Tests.Extraction;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

public class ApproveReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReceiptDate = new(2026, 10, 2);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly ApproveReceiptCommandHandler _handler;

    public ApproveReceiptTests()
    {
        _handler = new ApproveReceiptCommandHandler(new ApproveReceiptCommandValidator(), _receipts, new FixedTimeProvider(Now));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approving_a_ForReview_receipt_unchanged_writes_three_records_where_extracted_equals_final()
    {
        var receipt = await AddForReviewAsync();

        var response = await ApproveAsync(receipt, "Fake Cafe", ReceiptDate, 12.50m);

        Assert.Equal(receipt.TransactionNo, Assert.IsType<ApproveReceiptResponse.Approved>(response).TransactionNo);
        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
        Assert.Equal(3, _receipts.FieldRecords.Count);
        Assert.All(_receipts.FieldRecords, record =>
        {
            Assert.Equal(record.ExtractedValue, record.FinalValue);
            Assert.False(record.IsManual);
            Assert.Equal("test-model", record.Model);
            Assert.Equal("v1", record.PromptVersion);
        });
        Assert.Equal(
            ["Fake Cafe", "2026-10-02", "12.50"],
            _receipts.FieldRecords.OrderBy(r => r.FieldName).Select(r => r.FinalValue));
    }

    [Fact]
    public async Task A_corrected_total_gives_a_Total_record_that_differs_and_two_that_match()
    {
        var receipt = await AddForReviewAsync();

        await ApproveAsync(receipt, "Fake Cafe", ReceiptDate, 13.00m);

        var total = Assert.Single(_receipts.FieldRecords, r => r.FieldName == FieldName.Total);
        Assert.Equal("12.50", total.ExtractedValue);
        Assert.Equal("13.00", total.FinalValue);
        Assert.All(_receipts.FieldRecords.Where(r => r.FieldName != FieldName.Total), r => Assert.Equal(r.ExtractedValue, r.FinalValue));
        Assert.Equal(13.00m, receipt.Total);
        Assert.Equal(12.50m, receipt.ExtractedTotal);
    }

    [Fact]
    public async Task A_missing_extracted_date_that_the_user_fills_in_gives_a_record_with_a_null_extracted_value()
    {
        var receipt = await AddForReviewAsync((DateOnly?)null);

        await ApproveAsync(receipt, "Fake Cafe", ReceiptDate, 12.50m);

        var date = Assert.Single(_receipts.FieldRecords, r => r.FieldName == FieldName.Date);
        Assert.Null(date.ExtractedValue);
        Assert.Equal("2026-10-02", date.FinalValue);
    }

    [Fact]
    public async Task A_Failed_receipt_is_a_manual_entry_with_three_manual_records()
    {
        var receipt = await AddFailedAsync();

        var response = await ApproveAsync(receipt, "Typed Cafe", ReceiptDate, 8.25m, ReceiptCategory.Dining);

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        Assert.True(receipt.IsManualEntry);
        Assert.Equal(3, _receipts.FieldRecords.Count);
        Assert.All(_receipts.FieldRecords, record =>
        {
            Assert.True(record.IsManual);
            Assert.Null(record.ExtractedValue);
            Assert.Null(record.Model);
            Assert.Null(record.PromptVersion);
        });
    }

    [Theory]
    [InlineData(ReceiptStatus.Approved)]
    [InlineData(ReceiptStatus.Queued)]
    [InlineData(ReceiptStatus.Processing)]
    public async Task A_receipt_that_is_Approved_Queued_or_Processing_returns_conflict_and_writes_nothing(ReceiptStatus status)
    {
        var receipt = await AddReceiptWithStatusAsync(status);

        var response = await ApproveAsync(receipt, "Changed", ReceiptDate, 99.00m);

        Assert.IsType<ApproveReceiptResponse.Conflict>(response);
        Assert.Equal(status, receipt.Status);
        Assert.Empty(_receipts.FieldRecords);
        Assert.NotEqual("Changed", receipt.Merchant);
    }

    [Fact]
    public async Task An_unknown_id_returns_not_found()
    {
        var response = await _handler.Handle(
            new ApproveReceiptCommand(Guid.NewGuid(), "Cafe", ReceiptDate, 5m, ReceiptCategory.Dining), Token);

        Assert.IsType<ApproveReceiptResponse.NotFound>(response);
    }

    [Fact]
    public async Task Unknown_id_and_wrong_state_come_before_the_input_is_checked()
    {
        var approved = await AddReceiptWithStatusAsync(ReceiptStatus.Approved);

        var conflict = await _handler.Handle(new ApproveReceiptCommand(approved.Id, null, null, null, null), Token);
        var notFound = await _handler.Handle(new ApproveReceiptCommand(Guid.NewGuid(), null, null, null, null), Token);

        Assert.IsType<ApproveReceiptResponse.Conflict>(conflict);
        Assert.IsType<ApproveReceiptResponse.NotFound>(notFound);
    }

    [Fact]
    public async Task A_receipt_approved_in_another_tab_after_it_was_read_returns_conflict_and_writes_nothing()
    {
        var receipt = await AddForReviewAsync();
        _receipts.AfterFind = _ =>
        {
            // Another tab approves it between the first read and the transaction.
            _receipts.AfterFind = null;
            receipt.Approve("Other Tab", ReceiptDate, 1m, ReceiptCategory.Other, Now);
        };

        var response = await ApproveAsync(receipt, "This Tab", ReceiptDate, 2m);

        Assert.IsType<ApproveReceiptResponse.Conflict>(response);
        Assert.Equal("Other Tab", receipt.Merchant);
        Assert.Empty(_receipts.FieldRecords);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_merchant_is_invalid_and_writes_nothing(string? merchant)
    {
        var receipt = await AddForReviewAsync();

        var response = await ApproveAsync(receipt, merchant, ReceiptDate, 12.50m);

        AssertInvalid(response, "Merchant");
        AssertUntouched(receipt);
    }

    [Fact]
    public async Task A_missing_date_is_invalid_and_writes_nothing()
    {
        var receipt = await AddForReviewAsync();

        AssertInvalid(await ApproveAsync(receipt, "Cafe", null, 12.50m), "Date");
        AssertUntouched(receipt);
    }

    [Fact]
    public async Task A_missing_total_is_invalid_and_writes_nothing()
    {
        var receipt = await AddForReviewAsync();

        AssertInvalid(await ApproveAsync(receipt, "Cafe", ReceiptDate, null), "Total");
        AssertUntouched(receipt);
    }

    [Theory]
    [InlineData("12.345")]
    [InlineData("0.001")]
    [InlineData("99999999999999.99")]
    [InlineData("-99999999999999.99")]
    public async Task A_total_with_more_than_two_decimals_or_too_large_for_the_database_is_invalid(string total)
    {
        var receipt = await AddForReviewAsync();

        AssertInvalid(await ApproveAsync(receipt, "Cafe", ReceiptDate, decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture)), "Total");
        AssertUntouched(receipt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task A_missing_or_unknown_category_is_invalid_and_writes_nothing(int? category)
    {
        var receipt = await AddForReviewAsync();

        var response = await _handler.Handle(
            new ApproveReceiptCommand(receipt.Id, "Cafe", ReceiptDate, 12.50m, (ReceiptCategory?)category), Token);

        AssertInvalid(response, "Category");
        AssertUntouched(receipt);
    }

    [Fact]
    public async Task Every_problem_is_reported_at_once_each_for_its_own_field()
    {
        var receipt = await AddForReviewAsync();

        var response = await _handler.Handle(new ApproveReceiptCommand(receipt.Id, " ", null, null, null), Token);

        var invalid = Assert.IsType<ApproveReceiptResponse.Invalid>(response);
        Assert.Equal(["Category", "Date", "Merchant", "Total"], invalid.Errors.Select(e => e.Field).Order());
        Assert.All(invalid.Errors, e => Assert.NotEmpty(e.Message));
    }

    [Fact]
    public async Task A_flagged_receipt_can_be_approved()
    {
        var receipt = Receipt.Create("flagged.png", "image/png", 10, "flagged", "p", Now);
        receipt.StartProcessing(Now);
        receipt.CompleteExtraction("Cafe", null, 5000m, "test-model", "v1", isFlagged: true, "DateMissing,TotalAtOrAboveLimit", Now);
        Assert.True(await _receipts.TryAddAsync(receipt, Token));

        var response = await ApproveAsync(receipt, "Cafe", ReceiptDate, 5000m);

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("-5.00")]
    public async Task A_total_of_zero_or_less_can_be_approved(string total)
    {
        var receipt = await AddForReviewAsync();

        var response = await ApproveAsync(receipt, "Cafe", ReceiptDate, decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture));

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
    }

    [Theory]
    [InlineData("2030-01-01")]
    [InlineData("1999-12-31")]
    public async Task A_future_or_very_old_date_can_be_approved(string date)
    {
        var receipt = await AddForReviewAsync();

        var response = await ApproveAsync(receipt, "Cafe", DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), 12.50m);

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        Assert.Equal(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), receipt.ReceiptDate);
    }

    [Fact]
    public async Task The_merchant_is_trimmed()
    {
        var receipt = await AddForReviewAsync();

        await ApproveAsync(receipt, "  Fake Cafe  ", ReceiptDate, 12.50m);

        Assert.Equal("Fake Cafe", receipt.Merchant);
    }

    [Fact]
    public async Task The_approval_time_comes_from_the_clock()
    {
        var receipt = await AddForReviewAsync();

        await ApproveAsync(receipt, "Fake Cafe", ReceiptDate, 12.50m);

        Assert.Equal(Now, receipt.ApprovedAt);
        Assert.All(_receipts.FieldRecords, r => Assert.Equal(Now, r.CreatedAt));
    }

    private Task<ApproveReceiptResponse> ApproveAsync(
        Receipt receipt, string? merchant, DateOnly? date, decimal? total, ReceiptCategory category = ReceiptCategory.Dining) =>
        _handler.Handle(new ApproveReceiptCommand(receipt.Id, merchant, date, total, category), Token).AsTask();

    private Task<Receipt> AddForReviewAsync() => AddForReviewAsync(ReceiptDate);

    private async Task<Receipt> AddForReviewAsync(DateOnly? extractedDate) => await AddAsync(receipt =>
    {
        receipt.StartProcessing(Now);
        receipt.CompleteExtraction("Fake Cafe", extractedDate, 12.50m, "test-model", "v1", isFlagged: false, null, Now);
    });

    private async Task<Receipt> AddFailedAsync() => await AddAsync(receipt =>
    {
        receipt.StartProcessing(Now);
        receipt.FailAttempt("provider down", 1, Now);
    });

    private async Task<Receipt> AddReceiptWithStatusAsync(ReceiptStatus status) => await AddAsync(receipt =>
    {
        if (status == ReceiptStatus.Queued)
        {
            return;
        }

        receipt.StartProcessing(Now);
        if (status == ReceiptStatus.Approved)
        {
            receipt.CompleteExtraction("Fake Cafe", ReceiptDate, 12.50m, "test-model", "v1", false, null, Now);
            receipt.Approve("Fake Cafe", ReceiptDate, 12.50m, ReceiptCategory.Dining, Now);
        }
    });

    private async Task<Receipt> AddAsync(Action<Receipt> configure)
    {
        var n = _receipts.Receipts.Count;
        var receipt = Receipt.Create($"r{n}.png", "image/png", 10, $"checksum-{n}", $"path-{n}", Now.AddMinutes(-n));
        configure(receipt);
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        return receipt;
    }

    private static void AssertInvalid(ApproveReceiptResponse response, string field)
    {
        var invalid = Assert.IsType<ApproveReceiptResponse.Invalid>(response);
        Assert.Equal(field, Assert.Single(invalid.Errors).Field);
    }

    private void AssertUntouched(Receipt receipt)
    {
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.Null(receipt.Merchant);
        Assert.Empty(_receipts.FieldRecords);
    }
}
