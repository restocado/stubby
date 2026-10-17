using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.UploadReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>Uploads and processing through the real handlers, repository, file store and database. Only the model is stubbed.</summary>
public class ProcessNextReceiptTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private int _uploads;

    private Task<ProcessNextReceiptResult> ProcessAsync() => ProcessUntilAsync(Token);

    private Task<ProcessNextReceiptResult> ProcessUntilAsync(CancellationToken cancellationToken) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(new ProcessNextReceiptCommand(), cancellationToken));

    /// <summary>Uploads a small PNG with bytes of its own, so it is never a duplicate. Returns the new receipt's id.</summary>
    private async Task<Guid> UploadAsync()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, (byte)Interlocked.Increment(ref _uploads)];
        var response = await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new UploadReceiptCommand("receipt.png", "image/png", content.Length, new MemoryStream(content)), Token));
        return Assert.IsType<UploadReceiptResponse.Created>(response).Id;
    }

    [Fact]
    public async Task A_success_ends_ForReview_with_the_extracted_fields_stored()
    {
        Fixture.Extractor.Result = new ExtractionResult(
            "Lantern Noodle House", new DateOnly(2026, 10, 2), 32.62m, 26.50m, 2.12m, "gemma4:31b", "v1");
        var id = await UploadAsync();

        var result = await ProcessAsync();

        Assert.Equal(id, Assert.IsType<ProcessNextReceiptResult.Completed>(result).ReceiptId);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.Equal("Lantern Noodle House", receipt.ExtractedMerchant);
        Assert.Equal(new DateOnly(2026, 10, 2), receipt.ExtractedDate);
        Assert.Equal(32.62m, receipt.ExtractedTotal);
        Assert.Equal("gemma4:31b", receipt.Model);
        Assert.Equal("v1", receipt.PromptVersion);
        Assert.Equal(0, receipt.AttemptsUsed);
        Assert.Null(receipt.LastError);
    }

    [Fact]
    public async Task Subtotal_and_tax_are_stored_nowhere_in_the_database()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Subtotal = 11.50m, Tax = 1.00m };
        await UploadAsync();
        await ProcessAsync();

        var columns = await Fixture.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT lower(column_name) AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND (lower(column_name) LIKE '%subtotal%' OR lower(column_name) LIKE '%tax%')
                """)
            .ToListAsync(Token));

        Assert.Empty(columns);
    }

    [Fact]
    public async Task With_nothing_queued_nothing_changes()
    {
        var result = await ProcessAsync();

        Assert.IsType<ProcessNextReceiptResult.NothingToDo>(result);
        Assert.Equal(0, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task The_oldest_of_three_queued_receipts_is_processed_first()
    {
        var first = await UploadAsync();
        var second = await UploadAsync();
        var third = await UploadAsync();

        var order = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            order.Add(Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessAsync()).ReceiptId);
        }

        Assert.Equal([first, second, third], order);
    }

    [Fact]
    public async Task Total_at_or_above_the_limit_ends_ForReview_flagged()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Total = 3500.00m, Subtotal = null, Tax = null };
        var id = await UploadAsync();

        await ProcessAsync();

        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.True(receipt.IsFlagged);
        Assert.Equal("TotalAtOrAboveLimit", receipt.FlagReasons);
    }

    [Fact]
    public async Task A_missing_total_ends_ForReview_flagged_and_not_Failed()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Total = null, Subtotal = null, Tax = null };
        var id = await UploadAsync();

        await ProcessAsync();

        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.Equal("TotalMissing", receipt.FlagReasons);
    }

    [Fact]
    public async Task With_max_1_a_failing_extractor_ends_Failed_after_one_call()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 500.");
        var id = await UploadAsync();

        await ProcessAsync();

        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal(1, receipt.AttemptsUsed);
        Assert.Equal("The provider returned HTTP 500.", receipt.LastError);
        Assert.Equal(1, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task With_max_3_set_in_configuration_the_receipt_is_Queued_Queued_then_Failed()
    {
        Fixture.Configuration["Extraction:MaxAttempts"] = "3";
        Fixture.Extractor.Failure = new ExtractionException("The call timed out.");
        var id = await UploadAsync();

        var statuses = new List<ReceiptStatus>();
        for (var i = 0; i < 3; i++)
        {
            await ProcessAsync();
            statuses.Add((await Fixture.ReloadAsync(id)).Status);
        }

        Assert.Equal([ReceiptStatus.Queued, ReceiptStatus.Queued, ReceiptStatus.Failed], statuses);
        Assert.Equal(3, (await Fixture.ReloadAsync(id)).AttemptsUsed);
    }

    [Fact]
    public async Task A_missing_file_counts_as_a_failed_attempt()
    {
        var id = await UploadAsync();
        var path = Path.Combine(Fixture.ImageRoot, (await Fixture.ReloadAsync(id)).ImagePath);
        File.Delete(path);

        await ProcessAsync();

        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("The stored file is missing.", receipt.LastError);
        Assert.Equal(0, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task Cancelling_mid_extraction_leaves_the_receipt_Queued_with_no_attempt_charged()
    {
        var id = await UploadAsync();
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Fixture.Extractor.OnExtract = _ => shutdown.Cancel();
        Fixture.Extractor.Failure = new OperationCanceledException(shutdown.Token);

        var result = await ProcessUntilAsync(shutdown.Token);

        Assert.IsType<ProcessNextReceiptResult.Interrupted>(result);
        var receipt = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
        Assert.Equal(0, receipt.AttemptsUsed);
    }

    [Fact]
    public async Task Two_simultaneous_calls_with_one_queued_receipt_process_it_once()
    {
        await UploadAsync();

        var results = await Task.WhenAll(
            Task.Run(() => ProcessAsync(), Token),
            Task.Run(() => ProcessAsync(), Token));

        Assert.Single(results.OfType<ProcessNextReceiptResult.Completed>());
        Assert.Single(results.OfType<ProcessNextReceiptResult.NothingToDo>());
        Assert.Equal(1, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task A_receipt_left_Processing_is_not_picked_up_again()
    {
        await UploadAsync();
        await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));

        var result = await ProcessAsync();

        Assert.IsType<ProcessNextReceiptResult.NothingToDo>(result);
        Assert.Equal(0, Fixture.Extractor.Calls);
    }
}
