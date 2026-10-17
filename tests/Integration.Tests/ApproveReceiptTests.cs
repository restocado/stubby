using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>Approval through the real handler, repository and database, with the receipt read by the real worker step.</summary>
public class ApproveReceiptTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private static readonly DateOnly StubDate = new(2026, 10, 2);

    private Task<ApproveReceiptResponse> ApproveAsync(
        Guid id, string? merchant, DateOnly? date, decimal? total, ReceiptCategory? category = ReceiptCategory.Dining) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, merchant, date, total, category), Token));

    private Task<List<FieldRecord>> FieldRecordsAsync() =>
        Fixture.WithDbAsync(db => db.FieldRecords.AsNoTracking().OrderBy(r => r.FieldName).ToListAsync(Token));

    /// <summary>Uploads a receipt and has the worker step read it, so it is ForReview with the stub's values.</summary>
    private async Task<Guid> ReadAsync()
    {
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        return id;
    }

    [Fact]
    public async Task Approving_a_read_receipt_unchanged_stores_the_receipt_and_three_matching_records()
    {
        var id = await ReadAsync();

        var response = await ApproveAsync(id, "Fake Cafe", StubDate, 12.50m);

        var approved = Assert.IsType<ApproveReceiptResponse.Approved>(response);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(approved.TransactionNo, receipt.TransactionNo);
        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
        Assert.Equal("Fake Cafe", receipt.Merchant);
        Assert.Equal(StubDate, receipt.ReceiptDate);
        Assert.Equal(12.50m, receipt.Total);
        Assert.Equal(ReceiptCategory.Dining, receipt.Category);
        Assert.False(receipt.IsManualEntry);
        Assert.NotNull(receipt.ApprovedAt);

        var records = await FieldRecordsAsync();
        Assert.Equal([FieldName.Date, FieldName.Merchant, FieldName.Total], records.Select(r => r.FieldName));
        Assert.All(records, r =>
        {
            Assert.Equal(r.ExtractedValue, r.FinalValue);
            Assert.False(r.IsManual);
            Assert.Equal("stub-model:1b", r.Model);
            Assert.Equal("v1", r.PromptVersion);
        });
        Assert.Equal(["2026-10-02", "Fake Cafe", "12.50"], records.Select(r => r.FinalValue));
    }

    [Fact]
    public async Task A_corrected_total_keeps_the_extracted_value_beside_the_final_one()
    {
        var id = await ReadAsync();

        await ApproveAsync(id, "Fake Cafe", StubDate, 13.00m);

        var total = Assert.Single(await FieldRecordsAsync(), r => r.FieldName == FieldName.Total);
        Assert.Equal("12.50", total.ExtractedValue);
        Assert.Equal("13.00", total.FinalValue);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(13.00m, receipt.Total);
        Assert.Equal(12.50m, receipt.ExtractedTotal);
    }

    [Fact]
    public async Task A_date_the_user_fills_in_gives_a_record_with_no_extracted_value()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Date = null };
        var id = await ReadAsync();

        await ApproveAsync(id, "Fake Cafe", StubDate, 12.50m);

        var date = Assert.Single(await FieldRecordsAsync(), r => r.FieldName == FieldName.Date);
        Assert.Null(date.ExtractedValue);
        Assert.Equal("2026-10-02", date.FinalValue);
    }

    [Fact]
    public async Task A_Failed_receipt_is_approved_as_a_manual_entry()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());

        var response = await ApproveAsync(id, "Typed Cafe", StubDate, 8.25m, ReceiptCategory.Groceries);

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
        Assert.True(receipt.IsManualEntry);
        var records = await FieldRecordsAsync();
        Assert.Equal(3, records.Count);
        Assert.All(records, r =>
        {
            Assert.True(r.IsManual);
            Assert.Null(r.ExtractedValue);
            Assert.Null(r.Model);
            Assert.Null(r.PromptVersion);
        });
    }

    [Fact]
    public async Task Two_simultaneous_approvals_of_one_receipt_approve_it_once()
    {
        var id = await ReadAsync();

        var responses = await Task.WhenAll(
            Task.Run(() => ApproveAsync(id, "Tab One", StubDate, 1.00m), Token),
            Task.Run(() => ApproveAsync(id, "Tab Two", StubDate, 2.00m), Token));

        Assert.Single(responses.OfType<ApproveReceiptResponse.Approved>());
        Assert.Single(responses.OfType<ApproveReceiptResponse.Conflict>());
        Assert.Equal(3, (await FieldRecordsAsync()).Count);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Contains(receipt.Merchant, new[] { "Tab One", "Tab Two" });
    }

    [Fact]
    public async Task An_Approved_receipt_cannot_be_approved_again_and_nothing_more_is_written()
    {
        var id = await ReadAsync();
        await ApproveAsync(id, "Fake Cafe", StubDate, 12.50m);

        var again = await ApproveAsync(id, "Changed", StubDate, 99.00m);

        Assert.IsType<ApproveReceiptResponse.Conflict>(again);
        Assert.Equal("Fake Cafe", (await Fixture.ReloadAsync(id)).Merchant);
        Assert.Equal(3, (await FieldRecordsAsync()).Count);
    }

    [Fact]
    public async Task A_receipt_that_is_still_Queued_returns_conflict_and_writes_nothing()
    {
        var id = await UploadAsync();

        var response = await ApproveAsync(id, "Cafe", StubDate, 5m);

        Assert.IsType<ApproveReceiptResponse.Conflict>(response);
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(id)).Status);
        Assert.Empty(await FieldRecordsAsync());
    }

    [Fact]
    public async Task An_unknown_receipt_returns_not_found()
    {
        Assert.IsType<ApproveReceiptResponse.NotFound>(await ApproveAsync(Guid.NewGuid(), "Cafe", StubDate, 5m));
    }

    [Fact]
    public async Task Invalid_input_writes_nothing()
    {
        var id = await ReadAsync();

        var response = await ApproveAsync(id, " ", null, 12.345m, null);

        var invalid = Assert.IsType<ApproveReceiptResponse.Invalid>(response);
        Assert.Equal(["Category", "Date", "Merchant", "Total"], invalid.Errors.Select(e => e.Field).Order());
        Assert.Equal(ReceiptStatus.ForReview, (await Fixture.ReloadAsync(id)).Status);
        Assert.Empty(await FieldRecordsAsync());
    }

    [Fact]
    public async Task A_failure_while_saving_leaves_the_receipt_unchanged_and_writes_no_records()
    {
        var id = await ReadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InScopeAsync(async sp =>
            await sp.GetRequiredService<IReceiptRepository>().ApproveAsync(
                id,
                receipt =>
                {
                    // The receipt is changed and its records are built, and then the work fails before the save.
                    receipt.Approve("Half Done", StubDate, 1m, ReceiptCategory.Other, DateTimeOffset.UtcNow);
                    throw new InvalidOperationException("The disk caught fire.");
                },
                Token)));

        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.Null(receipt.Merchant);
        Assert.Empty(await FieldRecordsAsync());
        Assert.IsType<ApproveReceiptResponse.Approved>(await ApproveAsync(id, "Fake Cafe", StubDate, 12.50m));
    }

    [Fact]
    public async Task A_flagged_receipt_with_an_odd_total_and_date_can_be_approved()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Total = 5000.00m, Date = new DateOnly(2030, 1, 1) };
        var id = await ReadAsync();
        Assert.True((await Fixture.ReloadAsync(id)).IsFlagged);

        var response = await ApproveAsync(id, "Fake Cafe", new DateOnly(1999, 12, 31), 0m);

        Assert.IsType<ApproveReceiptResponse.Approved>(response);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(0m, receipt.Total);
        Assert.Equal(new DateOnly(1999, 12, 31), receipt.ReceiptDate);
    }

    [Fact]
    public async Task A_total_that_fills_the_column_round_trips()
    {
        var id = await ReadAsync();

        await ApproveAsync(id, "Big Spender", StubDate, 9_999_999_999_999.99m);

        Assert.Equal(9_999_999_999_999.99m, (await Fixture.ReloadAsync(id)).Total);
    }

    [Fact]
    public async Task FindById_returns_the_receipt_or_nothing()
    {
        var id = await UploadAsync();

        var found = await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().FindByIdAsync(id, Token));
        var missing = await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().FindByIdAsync(Guid.NewGuid(), Token));

        Assert.Equal(id, found!.Id);
        Assert.Null(missing);
    }
}
