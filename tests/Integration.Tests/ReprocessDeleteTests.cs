using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Application.Features.Receipts.DeleteReceipt;
using Stubby.Application.Features.Receipts.ReprocessAllFailedReceipts;
using Stubby.Application.Features.Receipts.ReprocessReceipt;
using Stubby.Application.Features.Receipts.UploadReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>Reprocess and delete through the real handlers, repository, file store and database.</summary>
public class ReprocessDeleteTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private static readonly DateOnly StubDate = new(2026, 10, 2);

    private Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command) =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(command, Token));

    private Task<ReprocessReceiptResponse> ReprocessAsync(Guid id) => SendAsync(new ReprocessReceiptCommand(id));

    private Task<DeleteReceiptResponse> DeleteAsync(Guid id) => SendAsync(new DeleteReceiptCommand(id));

    private Task<ApproveReceiptResponse> ApproveAsync(Guid id) =>
        SendAsync(new ApproveReceiptCommand(id, "Fake Cafe", StubDate, 12.50m, ReceiptCategory.Dining));

    private string FilePath(Receipt receipt) => Path.Combine(Fixture.ImageRoot, receipt.ImagePath);

    private Task<List<FieldRecord>> FieldRecordsAsync() =>
        Fixture.WithDbAsync(db => db.FieldRecords.AsNoTracking().OrderBy(r => r.FieldName).ToListAsync(Token));

    /// <summary>Uploads a receipt and has the worker step fail it, so it is Failed after one attempt.</summary>
    private async Task<Guid> FailedAsync()
    {
        var id = await UploadAsync();
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());
        Fixture.Extractor.Failure = null;
        return id;
    }

    private async Task<Guid> ForReviewAsync()
    {
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        return id;
    }

    [Fact]
    public async Task A_Failed_receipt_with_three_attempts_is_requeued_with_none_and_then_read_again()
    {
        Fixture.Configuration["Extraction:MaxAttempts"] = "3";
        Fixture.Extractor.Failure = new ExtractionException("The call timed out.");
        var id = await UploadAsync();
        for (var i = 0; i < 3; i++)
        {
            await ProcessOneAsync();
        }

        var failed = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Failed, failed.Status);
        Assert.Equal(3, failed.AttemptsUsed);
        Fixture.Extractor.Failure = null;

        var response = await ReprocessAsync(id);

        Assert.IsType<ReprocessReceiptResponse.Requeued>(response);
        var requeued = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Queued, requeued.Status);
        Assert.Equal(0, requeued.AttemptsUsed);
        Assert.Null(requeued.LastError);
        Assert.Equal(failed.CreatedAt, requeued.CreatedAt);
        Assert.Equal(failed.TransactionNo, requeued.TransactionNo);
        Assert.Equal(failed.ImagePath, requeued.ImagePath);

        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        Assert.Equal(ReceiptStatus.ForReview, (await Fixture.ReloadAsync(id)).Status);
    }

    [Fact]
    public async Task Receipts_that_are_not_Failed_return_conflict_and_are_unchanged()
    {
        var forReview = await ForReviewAsync();
        var approved = await ForReviewAsync();
        await ApproveAsync(approved);

        // Last, because the worker step always takes the oldest Queued receipt.
        var queued = await UploadAsync();

        foreach (var (id, status) in new[]
        {
            (queued, ReceiptStatus.Queued), (forReview, ReceiptStatus.ForReview), (approved, ReceiptStatus.Approved),
        })
        {
            Assert.IsType<ReprocessReceiptResponse.Conflict>(await ReprocessAsync(id));
            Assert.Equal(status, (await Fixture.ReloadAsync(id)).Status);
        }

        Assert.IsType<ReprocessReceiptResponse.NotFound>(await ReprocessAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Two_simultaneous_reprocesses_of_one_Failed_receipt_requeue_it_once()
    {
        var id = await FailedAsync();

        var responses = await Task.WhenAll(
            Task.Run(() => ReprocessAsync(id), Token),
            Task.Run(() => ReprocessAsync(id), Token));

        Assert.Single(responses.OfType<ReprocessReceiptResponse.Requeued>());
        Assert.Single(responses.OfType<ReprocessReceiptResponse.Conflict>());
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(id)).Status);
    }

    [Fact]
    public async Task All_requeues_five_Failed_receipts_and_leaves_two_ForReview_and_one_Approved_alone()
    {
        var failed = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            failed.Add(await FailedAsync());
        }

        var forReview = new[] { await ForReviewAsync(), await ForReviewAsync() };
        var approved = await ForReviewAsync();
        await ApproveAsync(approved);

        var response = await SendAsync(new ReprocessAllFailedReceiptsCommand());

        Assert.Equal(5, response.RequeuedCount);
        foreach (var id in failed)
        {
            var receipt = await Fixture.ReloadAsync(id);
            Assert.Equal(ReceiptStatus.Queued, receipt.Status);
            Assert.Equal(0, receipt.AttemptsUsed);
            Assert.Null(receipt.LastError);
        }

        foreach (var id in forReview)
        {
            Assert.Equal(ReceiptStatus.ForReview, (await Fixture.ReloadAsync(id)).Status);
        }

        Assert.Equal(ReceiptStatus.Approved, (await Fixture.ReloadAsync(approved)).Status);
    }

    [Fact]
    public async Task All_with_no_Failed_receipts_returns_zero()
    {
        await ForReviewAsync();

        Assert.Equal(0, (await SendAsync(new ReprocessAllFailedReceiptsCommand())).RequeuedCount);
    }

    [Theory]
    [InlineData(ReceiptStatus.ForReview)]
    [InlineData(ReceiptStatus.Failed)]
    [InlineData(ReceiptStatus.Approved)]
    public async Task A_receipt_that_is_ForReview_Failed_or_Approved_is_deleted_with_its_file(ReceiptStatus status)
    {
        var id = status == ReceiptStatus.Failed ? await FailedAsync() : await ForReviewAsync();
        if (status == ReceiptStatus.Approved)
        {
            await ApproveAsync(id);
        }

        var receipt = await Fixture.ReloadAsync(id);
        Assert.True(File.Exists(FilePath(receipt)));

        var response = await DeleteAsync(id);

        Assert.Equal(receipt.TransactionNo, Assert.IsType<DeleteReceiptResponse.Deleted>(response).TransactionNo);
        Assert.Equal(0, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));
        Assert.False(File.Exists(FilePath(receipt)));
    }

    [Fact]
    public async Task A_Queued_or_Processing_receipt_returns_conflict_and_removes_nothing()
    {
        var queued = await UploadAsync();
        var processing = await UploadAsync();
        var claimed = await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));
        Assert.Equal(queued, claimed!.Id);
        var queuedReceipt = await Fixture.ReloadAsync(queued);
        var processingReceipt = await Fixture.ReloadAsync(processing);

        Assert.IsType<DeleteReceiptResponse.Conflict>(await DeleteAsync(queued));
        Assert.IsType<DeleteReceiptResponse.Conflict>(await DeleteAsync(processing));

        Assert.Equal(2, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));
        Assert.True(File.Exists(FilePath(queuedReceipt)));
        Assert.True(File.Exists(FilePath(processingReceipt)));
    }

    [Fact]
    public async Task An_unknown_receipt_returns_not_found()
    {
        Assert.IsType<DeleteReceiptResponse.NotFound>(await DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Field_records_of_a_deleted_Approved_receipt_stay_unchanged()
    {
        var id = await ForReviewAsync();
        await ApproveAsync(id);
        var before = await FieldRecordsAsync();
        var countsBefore = await Fixture.WithDbAsync(db => db.FieldRecords.GroupBy(r => r.IsManual).Select(g => new { g.Key, N = g.Count() }).ToListAsync(Token));

        await DeleteAsync(id);

        var after = await FieldRecordsAsync();
        Assert.Equal(3, after.Count);
        Assert.Equal(
            before.Select(r => (r.Id, r.FieldName, r.ExtractedValue, r.FinalValue, r.IsManual, r.Model, r.PromptVersion, r.CreatedAt)),
            after.Select(r => (r.Id, r.FieldName, r.ExtractedValue, r.FinalValue, r.IsManual, r.Model, r.PromptVersion, r.CreatedAt)));
        var countsAfter = await Fixture.WithDbAsync(db => db.FieldRecords.GroupBy(r => r.IsManual).Select(g => new { g.Key, N = g.Count() }).ToListAsync(Token));
        Assert.Equal(countsBefore, countsAfter);
    }

    [Fact]
    public async Task The_same_file_can_be_uploaded_again_after_a_delete_and_gets_a_new_higher_number()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 4, 2];
        var first = await UploadAsync(content, "receipt.png", "image/png");
        var firstNumber = (await Fixture.ReloadAsync(first)).TransactionNo;
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());

        Assert.IsType<UploadReceiptResponse.Duplicate>(await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new UploadReceiptCommand("again.png", "image/png", content.Length, new MemoryStream(content)), Token)));
        Assert.IsType<DeleteReceiptResponse.Deleted>(await DeleteAsync(first));

        var again = await UploadAsync(content, "receipt.png", "image/png");

        var secondNumber = (await Fixture.ReloadAsync(again)).TransactionNo;
        Assert.True(secondNumber > firstNumber, $"The number {secondNumber} should be higher than the deleted {firstNumber}.");
    }

    [Fact]
    public async Task A_file_that_cannot_be_removed_does_not_stop_the_delete_from_succeeding()
    {
        var id = await ForReviewAsync();
        var path = FilePath(await Fixture.ReloadAsync(id));

        // On Windows an open file cannot be deleted, which is a real failed file delete.
        await using var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var response = await DeleteAsync(id);

        Assert.IsType<DeleteReceiptResponse.Deleted>(response);
        Assert.Equal(0, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(path));
        }
    }

    [Fact]
    public async Task A_delete_and_an_approval_of_the_same_receipt_never_leave_a_half_done_state()
    {
        var id = await ForReviewAsync();

        var (approve, delete) = await (
            Task.Run(() => ApproveAsync(id), Token),
            Task.Run(() => DeleteAsync(id), Token)).WhenAll();

        Assert.IsType<DeleteReceiptResponse.Deleted>(delete);
        Assert.Equal(0, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));

        // Either the approval ran first, and left its three records, or the delete did, and the approval found nothing.
        var records = await FieldRecordsAsync();
        if (approve is ApproveReceiptResponse.Approved)
        {
            Assert.Equal(3, records.Count);
        }
        else
        {
            Assert.IsType<ApproveReceiptResponse.NotFound>(approve);
            Assert.Empty(records);
        }
    }
}

internal static class TaskPairExtensions
{
    public static async Task<(T1, T2)> WhenAll<T1, T2>(this (Task<T1>, Task<T2>) tasks)
    {
        await Task.WhenAll(tasks.Item1, tasks.Item2);
        return (tasks.Item1.Result, tasks.Item2.Result);
    }
}
